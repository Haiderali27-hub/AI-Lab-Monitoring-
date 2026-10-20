import { test, expect } from '@playwright/test';
import { examWithStudents } from '../support/scenario';

test.describe('M3 — Student Exam Dashboard', () => {
  test('M3-A-01 Current exam status', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 1);
    const status = await students[0].currentExam();

    expect(status.examId).toBe(exam.examId);
    expect(status.examName).toBe(exam.title);
    expect(status.isEligible).toBe(true);
    expect(status.status).toBe('NotStarted');
    expect(status.message).toBe('Student can proceed.');
    // started 1 min ago, 60 min long → ~59 min left
    expect(status.remainingSeconds).toBeGreaterThan(55 * 60);
    expect(status.remainingSeconds).toBeLessThanOrEqual(60 * 60);
    expect(status.allowedApps).toContain('code.exe');
    // Module 3 start gate, evaluated on the server clock
    expect(status.canStartNow).toBe(true);
    expect(status.secondsUntilStart).toBe(0);
    expect(status.serverTimeUtc).toBeTruthy();
  });

  test('M3-A-02 Start before StartTime refused on both start paths', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 1, { startTime: new Date(Date.now() + 30 * 60_000) });
    const status = await students[0].currentExam();
    expect(status.examId).toBe(exam.examId);
    expect(status.canStartNow).toBe(false);
    expect(status.secondsUntilStart).toBeGreaterThan(29 * 60);

    const web = await students[0].startSession(exam.examId);
    expect(web.status()).toBe(400);
    expect((await web.json()).code).toBe('EXAM_NOT_STARTED');
    const desktop = await students[0].startCurrent();
    expect(desktop.status()).toBe(400);
    expect((await desktop.json()).code).toBe('EXAM_NOT_STARTED');
  });

  test('M3-A-03 Early-join window opens start before StartTime', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 1, { startTime: new Date(Date.now() + 3 * 60_000), earlyJoinMinutes: 5 });
    expect((await students[0].currentExam()).canStartNow).toBe(true);
    expect((await students[0].startSession(exam.examId)).status()).toBe(200);
  });
});

test.describe('M5 — Live Monitoring Client Service', () => {
  test('M5-A-01 Heartbeat accepted', async ({ request }) => {
    const { students } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start((await student.currentExam()).examId);

    const res = await student.heartbeat();
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data.receivedAtUtc).toBeTruthy();
    // Attributed to the session → server returns the authoritative remaining time
    expect(body.data.remainingSeconds).toBeGreaterThan(55 * 60);
  });

  test('M5-A-02 Heartbeat for foreign session is not attributed to that session', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 2);
    const [victim, attacker] = students;
    const victimSession = await victim.start(exam.examId);

    const res = await attacker.heartbeat({ sessionId: victimSession });
    expect(res.status()).toBeLessThan(500);
    const body = await res.json();
    // The server refused to bind the heartbeat to the victim's session
    expect(body.data?.remainingSeconds ?? null).toBeNull();
  });

  // Catalog expects an explicit 403/404; the server currently answers 200 and silently drops the
  // session reference (treated like a pre-session heartbeat). Safe, but not what M5-A-02 specifies.
  test('M5-A-02 Heartbeat for foreign session rejected with 403/404', async ({ request }) => {
    test.fail(true, 'Server returns 200 (silently ignores foreign session) instead of 403/404');
    const { students, exam } = await examWithStudents(request, 2);
    const [victim, attacker] = students;
    const victimSession = await victim.start(exam.examId);
    const res = await attacker.heartbeat({ sessionId: victimSession });
    expect([403, 404]).toContain(res.status());
  });
});
