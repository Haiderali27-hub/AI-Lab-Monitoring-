import { randomUUID } from 'node:crypto';
import { test, expect } from '@playwright/test';
import { examWithStudents } from '../support/scenario';
import { StaffApi } from '../support/staffApi';
import { API_URL } from '../support/env';

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

  // D-06 (fixed): a heartbeat, event, batch or capture naming another student's session (or an unknown one) is
  // refused with 404 and nothing is recorded against the victim.
  test('M5-A-02 Heartbeat for foreign session rejected with 403/404', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 2);
    const [victim, attacker] = students;
    const victimSession = await victim.start(exam.examId);
    for (const target of [victimSession, randomUUID()]) {
      expect((await attacker.heartbeat({ sessionId: target })).status()).toBe(404);
      expect((await attacker.violation('FocusLoss', 'chrome', target)).status()).toBe(404);
      expect((await attacker.eventBatch([{ clientEventId: randomUUID(), eventType: 'FocusLost' }], target)).status()).toBe(404);
      expect((await attacker.screenshot({ sessionId: target })).status()).toBe(404);
    }
    const timeline = await teacher.timeline(exam.examId, victimSession);
    expect(timeline.filter((i: any) => i.kind !== 'Status')).toEqual([]);
    expect((await teacher.live(exam.examId)).students).toEqual([]);   // the victim never heartbeated
  });

  test('M5-A-03 Screenshot upload is stored and only staff of the exam can read it', async ({ request }) => {
    const { students, exam, teacher, admin } = await examWithStudents(request, 1);
    const student = students[0];
    const sessionId = await student.start(exam.examId);

    const up = await student.screenshot({ trigger: 'Violation', reason: 'ProcessKilled' });
    expect(up.status(), await up.text()).toBe(200);
    const { screenshotId } = (await up.json()).data;

    const rows = await teacher.screenshots(exam.examId, sessionId);
    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ screenshotId, kind: 'Screen', trigger: 'Violation', triggerReason: 'ProcessKilled' });
    expect(rows[0].sha256).toMatch(/^[0-9a-f]{64}$/);

    const content = await teacher.screenshotContent(screenshotId);
    expect(content.status()).toBe(200);
    expect(content.headers()['content-type']).toContain('image/jpeg');
    expect((await content.body()).subarray(0, 3).toString('hex')).toBe('ffd8ff');

    // A file that is not an image is refused whatever it is called; students cannot read captures.
    const exe = Buffer.concat([Buffer.from('MZ'), Buffer.alloc(512)]);
    expect((await student.screenshot({ bytes: exe })).status()).toBe(400);
    const own = await request.get(`${API_URL}/api/screenshots/${screenshotId}/content`,
      { headers: { Authorization: `Bearer ${student.token}` } });
    expect(own.status()).toBe(403);

    // A teacher of another section is refused (privacy NFR).
    const outsiderUser = await admin.createUser('Teacher', 'Outsider');
    const outsider = await StaffApi.login(request, outsiderUser.email, outsiderUser.password);
    expect((await outsider.screenshotsRaw(exam.examId, sessionId)).status()).toBe(403);
    expect((await outsider.screenshotContent(screenshotId)).status()).toBe(403);
    expect((await outsider.timelineRaw(exam.examId, sessionId)).status()).toBe(403);

    // Nothing is captured after the exam is submitted.
    expect((await student.submit()).status()).toBe(200);
    const late = await student.screenshot();
    expect(late.status()).toBe(409);
    expect((await late.json()).code).toBe('SESSION_NOT_ACTIVE');
  });

  test('M5-A-04 Event batch is idempotent and appears on the session timeline', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const student = students[0];
    const sessionId = await student.start(exam.examId);
    expect((await student.heartbeat()).status()).toBe(200);

    const offlineAt = new Date(Date.now() - 5 * 60_000).toISOString();
    const events = [
      { clientEventId: randomUUID(), eventType: 'Violation', payload: { reason: 'FocusLoss', activeWindow: 'chrome' }, recordedAt: offlineAt },
      { clientEventId: randomUUID(), eventType: 'ProcessList', payload: { added: ['chrome'], removed: [] } },
      { clientEventId: randomUUID(), eventType: 'NetworkOffline', payload: { offlineFromUtc: offlineAt, offlineToUtc: new Date().toISOString() } },
    ];
    const first = await student.eventBatch(events);
    expect(first.status()).toBe(200);
    expect((await first.json()).data).toMatchObject({ accepted: 3, duplicates: 0 });
    // The reply was "lost" and the client resends the same batch: nothing is doubled.
    const again = await student.eventBatch(events);
    expect((await again.json()).data).toMatchObject({ accepted: 0, duplicates: 3 });

    const timeline = await teacher.timeline(exam.examId, sessionId);
    const types = timeline.map((i: any) => i.type);
    expect(types.filter((t: string) => t === 'FocusLost')).toHaveLength(1);
    expect(types).toEqual(expect.arrayContaining(['ProcessList', 'NetworkOffline', 'Started']));
    expect(types).not.toContain('Heartbeat');
    const focus = timeline.find((i: any) => i.type === 'FocusLost');
    expect(new Date(focus.at).getTime()).toBeCloseTo(new Date(offlineAt).getTime(), -3);   // client time kept
    expect(focus.severity).toBe('Warning');

    // Too many events in one call, or someone else's session, are refused.
    const tooMany = Array.from({ length: 201 }, () => ({ clientEventId: randomUUID(), eventType: 'ProcessList' }));
    expect((await student.eventBatch(tooMany)).status()).toBe(400);
    const other = (await examWithStudents(request, 1)).students[0];
    expect((await other.eventBatch(events, sessionId)).status()).toBe(404);
  });

  test('M5-A-05 Heartbeat ack carries the capture settings', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 1, { screenshotIntervalSeconds: 45 });
    const student = students[0];
    await student.start(exam.examId);
    const ack = (await (await student.heartbeat({ idleSeconds: 12, cameraStatus: 'Off' })).json()).data;
    expect(ack.screenshotIntervalSeconds).toBe(45);
    expect(ack.requireWebcam).toBe(false);
    expect(ack.webcamIntervalSeconds).toBeGreaterThanOrEqual(10);
  });

  test('M5-A-06 Webcam model findings (S8) reach the board and the timeline', async ({ request }) => {
    // What the desktop's on-device YuNet / YOLOX-Nano checks send: debounced episodes + the heartbeat camera state.
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const sessionId = await students[0].start(exam.examId);
    const batch = await students[0].eventBatch([
      { clientEventId: randomUUID(), eventType: 'MultipleFaces', payload: { detail: '2 faces in view' } },
      { clientEventId: randomUUID(), eventType: 'Violation', payload: { reason: 'PhoneDetected', detail: 'Mobile phone in view (confidence 70%)' } },
    ]);
    expect(batch.status()).toBe(200);
    expect((await students[0].heartbeat({ cameraStatus: 'LookingAway' })).status()).toBe(200);

    const tile = (await teacher.live(exam.examId)).students[0];
    expect(tile.cameraStatus).toBe('LookingAway');
    expect(tile.violationCount).toBe(2);
    expect(tile.tileStatus).toBe('Violation');            // two Suspicious findings escalate
    expect(['MultipleFaces', 'PhoneDetected']).toContain(tile.lastViolationType);

    const timeline = await teacher.timeline(exam.examId, sessionId);
    const multiple = timeline.find(i => i.type === 'MultipleFaces');
    expect(multiple?.severity).toBe('Suspicious');
    expect(multiple?.label).toBe('Multiple faces in view');
    expect(timeline.find(i => i.type === 'PhoneDetected')?.label).toBe('Mobile phone detected');

    // A camera state the server does not know is dropped, not stored.
    expect((await students[0].heartbeat({ cameraStatus: 'Smiling' as never })).status()).toBe(200);
    expect((await teacher.live(exam.examId)).students[0].cameraStatus ?? null).toBeNull();
  });
});
