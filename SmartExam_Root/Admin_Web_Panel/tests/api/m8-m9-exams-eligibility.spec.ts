import { test, expect } from '@playwright/test';
import { StaffApi, defaultQuestions } from '../support/staffApi';
import { examWithStudents } from '../support/scenario';
import { API_URL, uniqueTag } from '../support/env';

test.describe('M8 — Exam Creation & Configuration', () => {
  test('M8-A-01 Create exam via API', async ({ request }) => {
    const teacher = await StaffApi.teacher(request);
    const res = await teacher.createExamRaw({ questions: defaultQuestions().filter(q => q.type !== 'Theory') });
    expect(res.status()).toBe(201);
    const { examId } = await res.json();

    const detail = await teacher.examDetail(examId);
    expect(detail.status).toBe('Scheduled');
    expect(detail.questions.map((q: any) => q.type).sort()).toEqual(['Coding', 'Mcq']);
    const coding = detail.questions.find((q: any) => q.type === 'Coding');
    expect(coding.testCases).toHaveLength(2);
  });

  test('M8-A-02 Invalid MCQ index', async ({ request }) => {
    const teacher = await StaffApi.teacher(request);
    const res = await teacher.createExamRaw({
      questions: [{ type: 'Mcq', bodyText: 'Pick one', marks: 2, options: ['A', 'B'], correctOptionIndex: 5 }],
    });
    expect(res.status()).toBe(400);
    expect((await res.json()).message).toMatch(/correct option/i);
  });

  test('M8-A-03 Coding without test cases', async ({ request }) => {
    const teacher = await StaffApi.teacher(request);
    const res = await teacher.createExamRaw({
      questions: [{ type: 'Coding', bodyText: 'Write code', marks: 10, testCases: [] }],
    });
    expect(res.status()).toBe(400);
    expect((await res.json()).message).toMatch(/test case/i);
  });
});

test.describe('M9 — Eligibility & Access Control', () => {
  test('M9-A-01 Manual eligibility toggle', async ({ request }) => {
    const { students, exam, teacher, users } = await examWithStudents(request, 1);
    const set = await teacher.setEligibility(exam.examId, users[0].userId, false, 'Attendance below 75%');
    expect(set.status()).toBe(200);

    const status = await students[0].currentExam();
    expect(status.isEligible).toBe(false);
    expect(status.message).toBe('Attendance below 75%');

    // Both start paths are blocked
    const start = await students[0].startSession(exam.examId);
    expect(start.status()).toBe(400);
    expect((await start.json()).message).toMatch(/not eligible/i);
    const desktopStart = await students[0].startCurrent();
    expect(desktopStart.status()).toBe(400);
    expect((await desktopStart.json()).code).toBe('NOT_ELIGIBLE');

    // Re-enabling unblocks
    await teacher.setEligibility(exam.examId, users[0].userId, true);
    expect((await students[0].startSession(exam.examId)).status()).toBe(200);
  });

  test('M9-A-02 Attendance import + recalculate (header order free, unknown emails reported)', async ({ request }) => {
    const { students, exam, teacher, users } = await examWithStudents(request, 2);
    const { sectionId } = await teacher.examDetail(exam.examId);

    const res = await teacher.importAttendance(sectionId,
      `attended,email,total\n30,${users[0].email},40\n24,${users[1].email},40\n1,ghost@e2e.smartexam.test,2\n`);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.imported + body.updated).toBe(2);
    expect(JSON.stringify(body.errors)).toContain('ghost@e2e.smartexam.test');

    const recalc = await teacher.recalculateEligibility(exam.examId);
    expect(recalc.status()).toBe(200);
    const rows = await teacher.assignments(exam.examId);
    const ok = rows.find(r => r.userId === users[0].userId);
    const low = rows.find(r => r.userId === users[1].userId);
    expect(ok.attendancePercent).toBe(75);
    expect(ok.isEligible).toBe(true);
    expect(low.isEligible).toBe(false);
    expect(low.eligibilityNote).toBe('Attendance 60% < 75%');
    expect(low.eligibilitySource).toBe('Attendance');

    expect((await students[1].startCurrent()).status()).toBe(400);
    const computed = await (await StaffApi.admin(request)).auditLogs('ELIGIBILITY_COMPUTED');
    expect(computed.filter(l => l.details.includes(users[1].userId))).toHaveLength(1);

    // Students can't upload attendance
    const asStudent = await request.post(`${API_URL}/api/sections/${sectionId}/attendance/import`, {
      headers: { Authorization: `Bearer ${students[0].token}` },
      multipart: { file: { name: 'a.csv', mimeType: 'text/csv', buffer: Buffer.from('email,total,attended\n') } },
    });
    expect(asStudent.status()).toBe(403);
  });

  test('M9-A-03 Seat enforcement on the desktop start path', async ({ request }) => {
    const { students, exam, teacher, users } = await examWithStudents(request, 1);
    const seat = await teacher.workstation('R1C2');
    expect((await teacher.assignSeat(exam.examId, users[0].userId, seat.workstationId)).status()).toBe(200);

    // The simulator's random machine name is not R1C2 → refused, audited, no session.
    const refused = await students[0].startCurrent();
    expect(refused.status()).toBe(403);
    const body = await refused.json();
    expect(body.code).toBe('SEAT_MISMATCH');
    expect(body.message).toContain('R1C2');
    expect((await students[0].currentExam()).seatStatus).toBe('Mismatch');
    const audits = await (await StaffApi.admin(request)).auditLogs('SEAT_MISMATCH');
    expect(audits.some(l => l.details.includes(users[0].userId))).toBe(true);

    // From the assigned PC it starts.
    expect((await students[0].login(students[0].hwid, 'R1C2')).status()).toBe(200);
    expect((await students[0].startCurrent()).status()).toBe(200);
  });

  test('M9-A-04 Another teacher cannot read or change the roster, eligibility or seats', async ({ request }) => {
    const { exam, teacher, users } = await examWithStudents(request, 1);
    const admin = await StaffApi.admin(request);
    const tag = uniqueTag('t');
    const other = { name: `E2E Teacher ${tag}`, email: `${tag}@e2e.smartexam.test`, password: 'Teacher@123', role: 'Teacher' };
    expect((await admin.createUserRaw(other)).status()).toBe(201);
    const intruder = await StaffApi.login(request, other.email, other.password);

    expect((await intruder.get(`/api/exams/${exam.examId}/assignments`)).status()).toBe(403);
    expect((await intruder.put(`/api/exams/${exam.examId}/eligibility`, {
      assignments: [{ userId: users[0].userId, isEligible: false, eligibilityNote: 'not mine' }],
    })).status()).toBe(403);
    expect((await intruder.put(`/api/exams/${exam.examId}/assignments/${users[0].userId}/workstation`, { workstationId: null })).status()).toBe(403);
    expect((await intruder.delete(`/api/exams/${exam.examId}/assignments/${users[0].userId}`)).status()).toBe(403);

    // The owner sees the student still eligible.
    const row = (await teacher.assignments(exam.examId)).find(r => r.userId === users[0].userId);
    expect(row.isEligible).toBe(true);
  });
});
