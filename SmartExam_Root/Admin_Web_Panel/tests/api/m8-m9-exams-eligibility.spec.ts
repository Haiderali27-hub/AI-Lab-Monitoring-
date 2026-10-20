import { test, expect } from '@playwright/test';
import { StaffApi, defaultQuestions } from '../support/staffApi';
import { examWithStudents } from '../support/scenario';

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
});
