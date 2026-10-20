import { randomUUID } from 'node:crypto';
import { test, expect } from '@playwright/test';
import { examWithStudents } from '../support/scenario';
import { StaffApi } from '../support/staffApi';
import { API_URL } from '../support/env';

// Module 10 — the proctoring API behind the Live Monitor: incidents, the board snapshot and the apps a PC reported.
// (Hub methods — warnings, unlock, extra time — are covered by xUnit M10-I-* and the m10 E2E tests.)
test.describe('M10 — Live Proctoring API', () => {
  test('M10-A-01 Incident lifecycle, validation and scoping', async ({ request }) => {
    const { students, exam, teacher, admin } = await examWithStudents(request, 1);
    const sessionId = await students[0].start(exam.examId);

    const created = await teacher.createIncidentRaw(sessionId, 'High', 'Phone visible on desk');
    expect(created.status()).toBe(201);
    const incident = await created.json();
    expect(incident.status).toBe('Open');
    expect(incident.severity).toBe('High');
    expect(incident.studentName).toBeTruthy();

    expect((await teacher.updateIncidentRaw(incident.incidentId, { status: 'Escalated', notes: 'Needs the teacher' })).status()).toBe(200);
    const resolved = await teacher.updateIncidentRaw(incident.incidentId, { status: 'Resolved', notes: 'Phone handed in' });
    expect(resolved.status()).toBe(200);
    expect((await resolved.json()).statusNote).toBe('Phone handed in');

    // Resolved is final; unknown values are refused.
    const again = await teacher.updateIncidentRaw(incident.incidentId, { status: 'Open' });
    expect(again.status()).toBe(400);
    expect((await again.json()).code).toBe('INVALID_TRANSITION');
    expect((await teacher.updateIncidentRaw(incident.incidentId, { status: 'Banana' })).status()).toBe(400);
    expect((await teacher.createIncidentRaw(sessionId, 'Apocalyptic', 'x')).status()).toBe(400);
    expect((await teacher.createIncidentRaw(sessionId, 'Low', '   ')).status()).toBe(400);

    const list = await teacher.incidents(exam.examId);
    expect(list.map(i => i.incidentId)).toEqual([incident.incidentId]);
    expect(await teacher.incidents(exam.examId, 'Open')).toEqual([]);
    for (const eventType of ['INCIDENT_CREATED', 'INCIDENT_ESCALATED', 'INCIDENT_RESOLVED']) {
      const rows = (await admin.auditLogs(eventType)).filter((l: any) => l.entityId === incident.incidentId);
      expect(rows, eventType).toHaveLength(1);
    }

    // Another section's teacher and students get nothing.
    const otherUser = await admin.createUser('Teacher', 'Other');
    const other = await StaffApi.login(request, otherUser.email, otherUser.password);
    expect((await other.createIncidentRaw(sessionId, 'Low', 'x')).status()).toBe(403);
    expect((await other.incidentsRaw(exam.examId)).status()).toBe(403);
    const asStudent = await request.post(`${API_URL}/api/incidents`, {
      headers: { Authorization: `Bearer ${students[0].token}` },
      data: { sessionId, severity: 'Low', notes: 'x' },
    });
    expect(asStudent.status()).toBe(403);
  });

  test('M10-A-02 Board snapshot lists heartbeating students with their state', async ({ request }) => {
    const { students, users, exam, teacher, admin } = await examWithStudents(request, 2);
    const sessionId = await students[0].start(exam.examId);
    await students[1].start(exam.examId);                 // never heartbeats: not on the board yet
    expect((await students[0].heartbeat({ processListSnapshot: 'SmartExam,code', idleSeconds: 90, cameraStatus: 'Ok' })).status()).toBe(200);
    expect((await students[0].violation('KeyBlocked', 'Alt+Tab')).status()).toBe(200);
    expect((await students[0].screenshot()).status()).toBe(200);
    expect((await teacher.createIncidentRaw(sessionId, 'Low', 'Talking')).status()).toBe(201);

    const { students: board } = await teacher.live(exam.examId);
    expect(board).toHaveLength(1);
    const tile = board[0];
    expect(tile.studentName).toBe(users[0].name);
    expect(tile.sessionId).toBe(sessionId);
    expect(tile.violationCount).toBe(1);
    expect(tile.tileStatus).toBe('Warning');
    expect(tile.lastViolationType).toBe('KeyBlocked');
    expect(tile.processes).toBe('SmartExam,code');
    expect(tile.cameraStatus).toBe('Ok');
    expect(tile.latestScreenshot.screenshotId).toBeTruthy();
    expect(tile.openIncidents).toBe(1);
    expect(tile.unlockedUntilUtc).toBeNull();

    const otherUser = await admin.createUser('Teacher', 'Other');
    const other = await StaffApi.login(request, otherUser.email, otherUser.password);
    expect((await other.liveRaw(exam.examId)).status()).toBe(403);
  });

  test('M10-A-03 Apps seen during the session are remembered', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const sessionId = await students[0].start(exam.examId);
    await students[0].heartbeat({ processListSnapshot: 'SmartExam,chrome' });
    await students[0].heartbeat({ processListSnapshot: 'SmartExam,code' });

    const res = await teacher.processesRaw(exam.examId, sessionId);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.current).toEqual(['code', 'SmartExam']);
    const chrome = body.seen.find((s: any) => s.name === 'chrome');
    expect(chrome.running).toBe(false);
  });

  test('M10-A-04 Only the exam owner or an admin extends or ends the whole exam', async ({ request }) => {
    const { exam, teacher, admin } = await examWithStudents(request, 1);
    const before = await teacher.examDetail(exam.examId);
    const otherUser = await admin.createUser('Teacher', 'Other');
    const other = await StaffApi.login(request, otherUser.email, otherUser.password);

    expect((await other.extendTime(exam.examId, 30)).status()).toBe(403);
    expect((await other.forceSubmitAll(exam.examId)).status()).toBe(403);
    expect((await other.delete(`/api/exams/${exam.examId}`)).status()).toBe(403);
    // D-39: another teacher cannot open the exam at all any more (a co-proctor gets the view without the key, M10-A-05).
    expect((await other.get(`/api/exams/${exam.examId}`)).status()).toBe(403);
    const after = await teacher.examDetail(exam.examId);
    expect(after.durationMinutes).toBe(before.durationMinutes);
    expect(after.status).toBe(before.status);

    expect((await teacher.extendTime(exam.examId, 30)).status()).toBe(200);
    expect((await teacher.forceSubmitAll(exam.examId)).status()).toBe(200);
  });

  test('M10-A-05 Per-exam endpoints: owner/admin, proctors read-only, others 403, unknown 404 (D-39)', async ({ request }) => {
    const { students, exam, teacher, admin } = await examWithStudents(request, 1);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    const sessionId = await students[0].start(exam.examId);
    await students[0].saveAnswer(theory.questionId, 'A stack is LIFO, a queue is FIFO.');
    expect((await students[0].heartbeat()).status()).toBe(200);
    expect((await students[0].submit()).status()).toBe(200);
    const [row] = await teacher.results(exam.examId);
    const answerId = row.answers[0].answerId;
    const gen = await teacher.post(`/api/analytics/exams/${exam.examId}/report`);
    expect(gen.status()).toBe(200);
    const { downloadUrl } = await gen.json();

    const otherUser = await admin.createUser('Teacher', 'Other');
    const other = await StaffApi.login(request, otherUser.email, otherUser.password);
    const coUser = await admin.createUser('Teacher', 'CoProctor');
    expect((await teacher.assignProctor(exam.examId, coUser.userId)).status()).toBe(201);
    const coProctor = await StaffApi.login(request, coUser.email, coUser.password);

    const id = exam.examId;
    // Read / proctoring requests: owner, co-proctor 200; other teacher 403.
    const supervise = [
      `/api/exams/${id}`, `/api/exams/${id}/readiness`, `/api/exams/${id}/live`, `/api/exams/${id}/incidents`,
      `/api/exams/${id}/sessions/${sessionId}/timeline`, `/api/exams/${id}/sessions/${sessionId}/screenshots`,
    ];
    for (const path of supervise) {
      expect((await teacher.get(path)).status(), `owner ${path}`).toBe(200);
      expect((await coProctor.get(path)).status(), `co-proctor ${path}`).toBe(200);
      expect((await other.get(path)).status(), `other ${path}`).toBe(403);
    }
    const coView = await coProctor.examDetail(id);
    expect(coView.questions.every((q: any) => q.referenceAnswer == null && q.correctOptionIndex == null)).toBe(true);

    // Grades, analytics, reports, messaging and management: owner / admin only.
    const ownerOnly: [string, (api: StaffApi) => Promise<{ status(): number }>][] = [
      ['results', api => api.get(`/api/exams/${id}/results`)],
      ['plagiarism', api => api.get(`/api/exams/${id}/plagiarism`)],
      ['plagiarism matrix', api => api.get(`/api/exams/${id}/plagiarism/matrix`)],
      ['analytics summary', api => api.get(`/api/analytics/exams/${id}/summary`)],
      ['report history', api => api.get(`/api/analytics/exams/${id}/reports`)],
      ['report file', api => api.get(downloadUrl)],
      ['generate report', api => api.post(`/api/analytics/exams/${id}/report`)],
      ['override', api => api.post(`/api/answers/${answerId}/override`, { finalMarks: 1, note: 'hijack' })],
      ['regrade', api => api.post(`/api/answers/${answerId}/regrade`)],
      ['accept-ai', api => api.post(`/api/exams/${id}/grading/accept-ai`)],
      ['announce', api => api.post(`/api/notifications/exam/${id}/announce`, { title: 'Fake', message: 'Cancelled', sendEmail: false })],
      ['notify-grades', api => api.post(`/api/notifications/exam/${id}/notify-grades`)],
      ['assignments', api => api.get(`/api/exams/${id}/assignments`)],
      ['proctors', api => api.get(`/api/exams/${id}/proctors`)],
      ['extend-time', api => api.extendTime(id, 30)],
    ];
    for (const [name, call] of ownerOnly) {
      expect((await call(other)).status(), `other: ${name}`).toBe(403);
      expect((await call(coProctor)).status(), `co-proctor: ${name}`).toBe(403);
    }
    // Nothing happened on their behalf: no override, duration unchanged, no announcement reached the student.
    const [after] = await teacher.results(id);
    expect(after.answers[0].teacherOverride).toBeNull();
    expect((await teacher.examDetail(id)).durationMinutes).toBe(60);
    expect((await students[0].notifications()).notifications.filter((n: any) => n.type === 'Announcement')).toEqual([]);

    // Unknown exam / answer: 404, not 403.
    const unknown = randomUUID();
    for (const path of [`/api/exams/${unknown}`, `/api/exams/${unknown}/results`, `/api/exams/${unknown}/live`,
      `/api/analytics/exams/${unknown}/summary`, `/api/exams/${unknown}/incidents`]) {
      expect((await other.get(path)).status(), `unknown ${path}`).toBe(404);
    }
    expect((await other.post(`/api/answers/${unknown}/override`, { finalMarks: 1 })).status()).toBe(404);

    // The owner and an admin still can.
    expect((await teacher.get(downloadUrl)).status()).toBe(200);
    expect((await admin.get(`/api/analytics/exams/${id}/summary`)).status()).toBe(200);
  });
});
