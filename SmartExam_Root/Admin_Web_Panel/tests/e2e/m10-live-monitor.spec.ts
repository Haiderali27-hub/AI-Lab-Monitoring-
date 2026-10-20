import { randomUUID } from 'node:crypto';
import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import type { StudentClient } from '../support/studentClient';
import { acceptDialogs, examWithStudents, signInWeb } from '../support/scenario';

// Headless proctoring tests: the student desktop client is simulated over HTTP while a teacher
// browser page watches /teacher/live-monitor (SignalR hub /hubs/monitoring).

test.describe.configure({ timeout: 60_000 });

/**
 * Ends every other Active exam (the page shows the first Active one), creates a fresh exam with
 * `count` students, starts their sessions (start-session activates the exam) and opens the
 * Live Monitor as the teacher.
 */
async function openLiveExam(page: Page, request: APIRequestContext, count = 1) {
  await (await StaffApi.teacher(request)).endAllActiveExams();
  const scenario = await examWithStudents(request, count);
  for (const s of scenario.students) await s.start(scenario.exam.examId);
  await signInWeb(page, scenario.teacher);
  await page.goto('/teacher/live-monitor');
  await expect(page.getByText(`Live Monitor — ${scenario.exam.title}`)).toBeVisible();
  return scenario;
}

/** Keep heartbeating (like the desktop timer) until the tile shows up — absorbs hub-join latency. */
async function waitForTile(page: Page, student: StudentClient, name: string) {
  const tile = page.getByTestId('student-tile').filter({ hasText: name });
  await expect(async () => {
    expect((await student.heartbeat()).status()).toBe(200);
    await expect(tile).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 20_000 });
  return tile;
}

test.describe('M10 — Live Proctoring Dashboard', () => {
  test('M10-E-01 Live monitor loads', async ({ page, request }) => {
    const teacher = await StaffApi.teacher(request);
    await teacher.endAllActiveExams();
    await signInWeb(page, teacher);

    // No active exam → explicit empty state, no crash
    await page.goto('/teacher/live-monitor');
    await expect(page.getByRole('heading', { name: 'No Exam Currently Active' })).toBeVisible();
    await expect(page.getByText(/cannot load|something went wrong/i)).toHaveCount(0);

    // With an active exam → header, controls and waiting state
    const scenario = await examWithStudents(request, 1);
    await scenario.students[0].start(scenario.exam.examId);
    await page.reload();
    await expect(page.getByText(`Live Monitor — ${scenario.exam.title}`)).toBeVisible();
    await expect(page.getByRole('button', { name: /extend time/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /force end exam/i })).toBeVisible();
    await expect(page.getByRole('heading', { name: /waiting for students/i })).toBeVisible();
  });

  test('M10-E-02 Student tile appears on heartbeat', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    await expect(tile).toContainText(students[0].machineName);
    await expect(tile).toContainText('0/3');          // progress: answered / total questions
    await expect(page.getByText('1 Student(s) Active')).toBeVisible();
  });

  // Regression: the desktop client starts via POST /api/exams/student/start, which used to leave the
  // exam Scheduled — so Live Monitor showed "No Exam Currently Active" for WPF-started exams.
  test('M10-E-02 Student tile appears when started via the desktop start path', async ({ page, request }) => {
    await (await StaffApi.teacher(request)).endAllActiveExams();
    const { students, users, teacher } = await examWithStudents(request, 1);
    expect((await students[0].startCurrent()).status()).toBe(200);
    await signInWeb(page, teacher);
    await page.goto('/teacher/live-monitor');
    const tile = page.getByTestId('student-tile').filter({ hasText: users[0].name });
    await expect(async () => {
      await students[0].heartbeat();
      await expect(tile).toBeVisible({ timeout: 1_000 });
    }).toPass({ timeout: 8_000 });
  });

  // Server-side ViolationClassifier: Warning → Suspicious → Violation (shown as "Critical").
  test('M10-E-03 Status progression', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const student = students[0];
    const tile = await waitForTile(page, student, users[0].name);
    const status = tile.getByTestId('tile-status');
    await expect(status).toHaveText('Stable');

    // Blocked shortcut → Warning (live from the ViolationEvent, confirmed by the next heartbeat)
    await student.violation('KeyBlocked', 'SmartExam - Exam Window');
    await expect(status).toHaveText('Warning', { timeout: 5_000 });
    await student.heartbeat();
    await expect(status).toHaveText('Warning');

    // Unauthorized app reported → Suspicious
    await student.event('Violation', { reason: 'UnauthorizedProcess', detail: 'chrome (Reported)' });
    await expect(status).toHaveText('Suspicious', { timeout: 5_000 });

    // App actually launched and killed → Violation
    await student.event('Violation', { reason: 'ProcessKilled', detail: 'chrome (Killed)' });
    await expect(status).toHaveText('Critical', { timeout: 5_000 });
    await student.heartbeat();
    await expect(status).toHaveText('Critical');
  });

  test('M10-E-04 Send warning', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    const message = `Eyes on your own screen ${Date.now()}`;

    await tile.click();
    const drawer = page.getByTestId('student-drawer');
    await expect(drawer).toContainText(users[0].name);
    await drawer.getByTestId('warning-message-input').fill(message);
    await drawer.getByTestId('send-warning-button').click();
    await expect(drawer.getByText('Warning Message Sent')).toBeVisible();

    await expect.poll(async () => {
      const { notifications } = await students[0].notifications();
      return notifications.find(n => n.body === message)?.title ?? null;
    }, { timeout: 10_000 }).toBe('Proctor Warning');
  });

  test('M10-E-05 Extend time', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    await waitForTile(page, students[0], users[0].name);
    const before = (await (await students[0].heartbeat()).json()).data.remainingSeconds as number;

    const dialogs = acceptDialogs(page, '10');
    await page.getByRole('button', { name: /extend time/i }).click();
    await expect.poll(() => dialogs.join('\n')).toContain('Exam extended by 10 minutes');

    const after = (await (await students[0].heartbeat()).json()).data.remainingSeconds as number;
    expect(after - before).toBeGreaterThan(570);
    expect(after - before).toBeLessThanOrEqual(600);
    expect((await students[0].currentExam()).remainingSeconds).toBeGreaterThan(before + 570);
  });

  test('M10-E-06 Detail drawer', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    await students[0].heartbeat({ processListSnapshot: 'SmartExam,code,chrome' });
    const tile = await waitForTile(page, students[0], users[0].name);
    await students[0].violation('KeyBlocked', 'SmartExam - Exam Window');
    await tile.click();
    const drawer = page.getByTestId('student-drawer');
    await expect(drawer.getByTestId('activity-timeline')).toBeVisible();
    // chrome was in an earlier heartbeat only: the drawer lists apps seen during the session, not just the last beat.
    await expect(drawer.getByTestId('process-list')).toContainText('chrome');
    await expect(drawer.getByTestId('process-list')).toContainText('explorer');
    // The timeline is the server's: it has the violation and the start, and survives a reload.
    await expect(drawer.getByTestId('activity-timeline')).toContainText('Blocked keys', { timeout: 10_000 });
    await page.reload();
    await page.getByTestId('student-tile').filter({ hasText: users[0].name }).click();
    await expect(page.getByTestId('activity-timeline')).toContainText('Blocked keys');
    await expect(page.getByTestId('activity-timeline')).toContainText('Exam started');
  });

  test('M10-E-07 Mark incident', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    await tile.click();
    await page.getByRole('button', { name: /mark incident/i }).click();
    await page.getByLabel(/description/i).fill('Phone on desk');
    await page.getByRole('button', { name: /save incident/i }).click();
    await expect(tile.getByTestId('incident-badge')).toBeVisible();
    await expect(page.getByTestId('incident-list')).toContainText('Phone on desk');

    // Resolving it clears the badge; the change is audited.
    await page.getByRole('button', { name: /mark resolved/i }).click();
    await page.getByLabel(/resolution note/i).fill('Phone handed to the invigilator');
    await page.getByRole('button', { name: /^resolve$/i }).click();
    await expect(tile.getByTestId('incident-badge')).toHaveCount(0);
    await expect(page.getByTestId('incident-list')).toContainText('Phone handed to the invigilator');
  });

  test('M10-E-08 Screenshot carousel', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    // The simulator uploads one capture (POST /api/monitoring/screenshots, M5-I-01).
    expect((await students[0].screenshot()).status()).toBe(200);
    await tile.click();
    await expect(page.getByTestId('screenshot-carousel').locator('img')).toHaveCount(1);
    await expect.poll(() => page.getByTestId('screenshot-carousel').locator('img').evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth))
      .toBeGreaterThan(0);
    // A violation capture arriving while the drawer is open joins the strip.
    expect((await students[0].screenshot({ trigger: 'Violation', reason: 'UnauthorizedProcess' })).status()).toBe(200);
    await expect(page.getByTestId('screenshot-carousel').locator('img')).toHaveCount(2, { timeout: 10_000 });
  });

  test('M10-E-09 Unlock button', async ({ page, request }) => {
    const { students, users, admin } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    await tile.click();
    await page.getByRole('button', { name: /unlock 5 min/i }).click();
    await expect(page.getByText(/unlocked for 5 minutes/i)).toBeVisible();
    expect((await admin.auditLogs('SESSION_UNLOCKED')).length).toBeGreaterThan(0);

    // The desktop client learns it from its next heartbeat ack; the tile says so; "Lock again" ends it.
    const until = (await (await students[0].heartbeat()).json()).data.unlockedUntilUtc as string;
    expect(new Date(until).getTime() - Date.now()).toBeGreaterThan(4 * 60_000);
    await expect(tile.getByTestId('tile-unlocked')).toBeVisible();
    await page.getByRole('button', { name: /lock again/i }).click();
    await expect(tile.getByTestId('tile-unlocked')).toHaveCount(0);
    expect((await (await students[0].heartbeat()).json()).data.unlockedUntilUtc).toBeNull();
  });

  test('M10-E-13 Warning reaches the desktop client until acknowledged (D-05)', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    const message = `Keep your eyes on your screen ${Date.now()}`;
    await tile.click();
    await page.getByTestId('warning-message-input').fill(message);
    await page.getByTestId('send-warning-button').click();
    await expect(page.getByText('Warning Message Sent')).toBeVisible();

    // The WPF app has no SignalR client: its heartbeat ack carries the warning, every beat, until acknowledged.
    const pending = async () => ((await (await students[0].heartbeat()).json()).data.warnings as any[]).filter(w => w.body === message);
    const [warning] = await pending();
    expect(warning.title).toBe('Proctor Warning');
    expect(await pending()).toHaveLength(1);
    expect((await students[0].markNotificationRead(warning.notificationId)).status()).toBe(200);
    expect(await pending()).toHaveLength(0);
    // The drawer's server timeline shows the warning to every proctor.
    await expect(page.getByTestId('activity-timeline')).toContainText(message, { timeout: 10_000 });
  });

  test('M10-E-14 Board survives a reload; readiness shown on the tile', async ({ page, request }) => {
    const { students, users, exam } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);   // the page has joined the exam's group
    expect((await students[0].readiness(exam.examId)).status()).toBe(200);
    await expect(tile.getByTestId('tile-readiness')).toHaveText('Ready', { timeout: 10_000 });
    await students[0].violation('KeyBlocked', 'Alt+Tab');
    await expect(tile).toContainText('1 ALERTS', { timeout: 5_000 });
    expect((await students[0].screenshot()).status()).toBe(200);
    await expect(tile.getByTestId('tile-thumbnail')).toBeVisible({ timeout: 10_000 });

    // No heartbeat after the reload: the tile comes from the server's snapshot (GET /exams/{id}/live).
    await page.reload();
    const again = page.getByTestId('student-tile').filter({ hasText: users[0].name });
    await expect(again).toBeVisible();
    await expect(again).toContainText('1 ALERTS');
    await expect(again.getByTestId('tile-status')).toHaveText('Warning');
    await expect(again.getByTestId('tile-readiness')).toHaveText('Ready');
    await expect(again.getByTestId('tile-thumbnail')).toHaveAttribute('src', /^blob:/);
    await again.click();
    await expect(page.getByTestId('drawer-readiness')).toContainText('All checks passed');
  });

  test('M10-E-10 Invigilator restricted view', async ({ page, request }) => {
    // Two running exams; the invigilator is assigned to exam A only.
    const tag = Date.now().toString(36);
    const a = await examWithStudents(request, 1, { title: `Exam A ${tag}` });
    const b = await examWithStudents(request, 1, { title: `Exam B ${tag}` });
    await a.students[0].start(a.exam.examId);
    await b.students[0].start(b.exam.examId);
    const invigilatorUser = await a.admin.createInvigilator();
    expect((await a.teacher.assignProctor(a.exam.examId, invigilatorUser.userId)).status()).toBe(201);

    const invigilator = await StaffApi.login(request, invigilatorUser.email, invigilatorUser.password);
    await signInWeb(page, invigilator);
    await page.goto('/invigilator/live-monitor');
    await expect(page.getByText(`Live Monitor — ${a.exam.title}`)).toBeVisible();

    // Only the assigned exam is offered; exam B never reaches this browser.
    const picker = page.getByTestId('exam-picker');
    await expect(picker.getByRole('option')).toHaveText([new RegExp(`Exam A ${tag}`)]);
    await expect(page.getByRole('option', { name: /exam b/i })).toHaveCount(0);

    // Supervising works (hub group joined → tiles), exam-wide controls are the teacher's.
    await waitForTile(page, a.students[0], a.users[0].name);
    await expect(page.getByRole('button', { name: /extend time/i })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /force end exam/i })).toHaveCount(0);

    // Asking for exam B by URL does not switch to it.
    await page.goto(`/invigilator/live-monitor?examId=${b.exam.examId}`);
    await expect(page.getByText(`Live Monitor — ${a.exam.title}`)).toBeVisible();
  });

  test('M10-E-11 Teacher picks between running exams', async ({ page, request }) => {
    const teacher = await StaffApi.teacher(request);
    await teacher.endAllActiveExams();
    const first = await examWithStudents(request, 1);
    const second = await examWithStudents(request, 1);
    await first.students[0].start(first.exam.examId);
    await second.students[0].start(second.exam.examId);

    await signInWeb(page, teacher);
    await page.goto(`/teacher/live-monitor?examId=${second.exam.examId}`);
    await expect(page.getByText(`Live Monitor — ${second.exam.title}`)).toBeVisible();
    await waitForTile(page, second.students[0], second.users[0].name);

    await page.getByTestId('exam-picker').selectOption(first.exam.examId);
    await expect(page.getByText(`Live Monitor — ${first.exam.title}`)).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`examId=${first.exam.examId}`));
    // The board belongs to the selected exam only.
    await waitForTile(page, first.students[0], first.users[0].name);
    await expect(page.getByTestId('student-tile').filter({ hasText: second.users[0].name })).toHaveCount(0);
  });

  test('M10-E-12 Live monitor opens an exam the teacher may supervise by default', async ({ page, request }) => {
    const teacher = await StaffApi.teacher(request);
    await teacher.endAllActiveExams();
    // Two running exams of the seeded teacher's section; another teacher co-proctors only the second.
    const notMine = await examWithStudents(request, 1);
    const assigned = await examWithStudents(request, 1);
    await notMine.students[0].start(notMine.exam.examId);
    await assigned.students[0].start(assigned.exam.examId);
    const coUser = await notMine.admin.createUser('Teacher', 'CoProctor');
    expect((await teacher.assignProctor(assigned.exam.examId, coUser.userId)).status()).toBe(201);
    const coProctor = await StaffApi.login(request, coUser.email, coUser.password);
    const flags = new Map((await coProctor.exams()).map(e => [e.examId, e.canSupervise]));
    expect(flags.get(notMine.exam.examId)).toBe(false);
    expect(flags.get(assigned.exam.examId)).toBe(true);

    // No ?examId=: the board opens on the exam they supervise, not on one the hub would refuse.
    await signInWeb(page, coProctor);
    await page.goto('/teacher/live-monitor');
    await expect(page.getByText(`Live Monitor — ${assigned.exam.title}`)).toBeVisible();
    await waitForTile(page, assigned.students[0], assigned.users[0].name);
    await expect(page.getByTestId('join-denied')).toHaveCount(0);
  });
});

test.describe('M5 — Live Monitoring Client Service (dashboard side)', () => {
  test('M5-E-01 Heartbeat shows student online', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    await expect(tile.getByTestId('tile-status')).toHaveText('Stable');
    await expect(tile).toContainText('SmartExam - Exam Window'); // active window from the heartbeat
    await expect(tile).not.toContainText('ALERTS');
  });

  test('M5-E-02 Thumbnail on tile', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    await expect(tile.getByTestId('tile-thumbnail')).toHaveCount(0);

    // The simulator uploads a capture (ScreenshotService → POST /api/monitoring/screenshots); the page is told
    // over SignalR and fetches the image with the teacher's token.
    expect((await students[0].screenshot()).status()).toBe(200);
    const thumbnail = tile.getByTestId('tile-thumbnail');
    await expect(thumbnail).toBeVisible({ timeout: 10_000 });
    await expect(thumbnail).toHaveAttribute('src', /^blob:/);
    await expect.poll(() => thumbnail.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth)).toBeGreaterThan(0);

    // A violation capture replaces it and is noted in the drawer's timeline.
    expect((await students[0].screenshot({ trigger: 'Violation', reason: 'UnauthorizedProcess' })).status()).toBe(200);
    await tile.click();
    await expect(page.getByTestId('student-drawer')).toContainText('Evidence captured for: Unauthorized app', { timeout: 10_000 });
  });

  test('M5-E-03 Disconnected tile', async ({ page, request }) => {
    test.setTimeout(90_000);
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    await expect(tile.getByTestId('tile-status')).toHaveText('Stable');
    // Simulator stops heartbeating; the server's watchdog flags the session after 30 s (sweep every 5 s, M5-I-08)
    await expect(tile.getByTestId('tile-status')).toHaveText(/disconnected/i, { timeout: 40_000 });

    // The next heartbeat brings the student back.
    expect((await students[0].heartbeat()).status()).toBe(200);
    await expect(tile.getByTestId('tile-status')).toHaveText('Stable', { timeout: 10_000 });
    await tile.click();
    await expect(page.getByTestId('student-drawer')).toContainText('Reconnected');
  });

  test('M5-E-04 Camera, idle time and replayed violations reach the tile', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);

    // Heartbeat with what the desktop client measures: input idle time and the webcam state.
    expect((await students[0].heartbeat({ idleSeconds: 185, cameraStatus: 'Covered' })).status()).toBe(200);
    await expect(tile.getByTestId('tile-idle')).toHaveText('Idle 3m');
    await expect(tile.getByTestId('tile-camera')).toHaveText('Camera covered');

    // A fresh violation from the offline outbox is shown live; an old replayed one only counts.
    const batch = await students[0].eventBatch([
      { clientEventId: randomUUID(), eventType: 'CameraCovered', payload: { detail: 'lens taped' } },
      { clientEventId: randomUUID(), eventType: 'Violation', payload: { reason: 'KeyBlocked', detail: 'Alt+Tab' }, recordedAt: new Date(Date.now() - 10 * 60_000).toISOString() },
    ]);
    expect(batch.status()).toBe(200);
    await expect(tile.getByTestId('tile-last-violation')).toHaveText('Camera covered: lens taped', { timeout: 5_000 });
    await students[0].heartbeat();
    await expect(tile).toContainText('2 ALERTS');
    await expect(tile.getByTestId('tile-status')).toHaveText('Suspicious');
  });
});

test.describe('M4 — Secure Exam Environment (dashboard side)', () => {
  test('M4-E-01 Violation appears live', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);
    await expect(tile.getByTestId('tile-status')).toHaveText('Stable');

    // A single focus loss is a Warning (M4-I-03), shown live
    expect((await students[0].violation('FocusLoss', 'Google Chrome')).status()).toBe(200);
    await expect(tile.getByTestId('tile-status')).toHaveText('Warning', { timeout: 5_000 });
    await expect(tile).toContainText('1 ALERTS');

    // A severe violation turns it red, and the next heartbeat (server-side classification) keeps it red
    expect((await students[0].violation('UsbInserted', 'E:')).status()).toBe(200);
    await expect(tile.getByTestId('tile-status')).toHaveText('Critical', { timeout: 5_000 });
    await expect(tile).toContainText('2 ALERTS');
    await students[0].heartbeat();
    await expect(tile.getByTestId('tile-status')).toHaveText('Critical');
  });

  test('M4-E-02 Violation type label shown', async ({ page, request }) => {
    const { students, users } = await openLiveExam(page, request);
    const tile = await waitForTile(page, students[0], users[0].name);

    // Both client forms: generic "Violation" + reason, and the specific type as eventType.
    await students[0].event('Violation', { reason: 'KeyBlocked', detail: 'Alt+Tab' });
    await expect(tile.getByTestId('tile-last-violation')).toHaveText('Blocked keys: Alt+Tab', { timeout: 5_000 });

    await students[0].event('UnauthorizedProcess', { detail: 'chrome (Killed)' });
    await expect(tile.getByTestId('tile-last-violation')).toHaveText('Unauthorized app: chrome (Killed)', { timeout: 5_000 });

    // The label survives the next heartbeat
    await students[0].heartbeat();
    await expect(tile.getByTestId('tile-last-violation')).toContainText('Unauthorized app');
  });

  test('M4-E-03 Create exam lockdown settings', async ({ page, request }) => {
    const teacher = await StaffApi.teacher(request);
    await signInWeb(page, teacher);
    const title = `Lockdown Exam ${Date.now()}`;
    await page.goto('/teacher/create-exam');

    // Step 1 — details
    await page.getByLabel(/exam title/i).fill(title);
    await page.getByLabel(/section/i).selectOption({ index: 1 });
    const tomorrow = new Date(Date.now() + 24 * 3600_000).toISOString().split('T')[0];
    await page.getByLabel(/date/i).fill(tomorrow);
    await page.getByLabel(/time/i).fill('10:00');
    await page.getByRole('button', { name: /next step/i }).click();

    // Step 2 — one theory question
    await page.getByRole('button', { name: /add question/i }).first().click();
    await page.locator('select').filter({ has: page.locator('option[value="Mcq"]') }).first().selectOption('Theory');
    await page.getByPlaceholder(/question prompt|coding task/i).first().fill('Explain a stack.');
    await page.getByRole('button', { name: /next step/i }).click();

    // Step 3 — lockdown settings
    await expect(page.getByLabel(/lockdown mode/i)).toHaveValue('Report');   // safe default
    await page.getByLabel(/lockdown mode/i).selectOption('Enforce');
    await page.getByLabel(/flag multiple monitors/i).uncheck();
    await page.getByRole('button', { name: /next step/i }).click();
    await page.getByRole('button', { name: 'Create Exam' }).click();
    await expect(page).toHaveURL(/\/teacher\/dashboard/);

    const created = (await teacher.exams()).find((e: any) => e.title === title);
    expect(created, 'exam created via the wizard').toBeTruthy();
    const detail = await teacher.examDetail(created.examId);
    expect(detail.lockdownMode).toBe('Enforce');
    expect(detail.blockMultiMonitor).toBe(false);

    // Edit page shows the saved values
    await page.goto(`/teacher/exams/${created.examId}/edit`);
    await page.getByRole('button', { name: /next step/i }).click();
    await page.getByRole('button', { name: /next step/i }).click();
    await expect(page.getByLabel(/lockdown mode/i)).toHaveValue('Enforce');
    await expect(page.getByLabel(/flag multiple monitors/i)).not.toBeChecked();
  });
});

test.describe('M9 — Eligibility & Access Control (dashboard side)', () => {
  test('M9-E-05 Seat mismatch alert', async ({ page, request }) => {
    const teacher = await StaffApi.teacher(request);
    await teacher.endAllActiveExams();
    const { students, users, exam } = await examWithStudents(request, 1);
    const r1c1 = await teacher.workstation('R1C1');
    expect((await teacher.assignSeat(exam.examId, users[0].userId, r1c1.workstationId)).status()).toBe(200);

    // Seat R1C1 assigned: the simulator starts from R1C1 (start activates the exam) ...
    expect((await students[0].login(students[0].hwid, 'R1C1')).status()).toBe(200);
    await students[0].start(exam.examId);
    await signInWeb(page, teacher);
    await page.goto('/teacher/live-monitor');
    await expect(page.getByText(`Live Monitor — ${exam.title}`)).toBeVisible();

    // ... then the same bound student shows up from machine R1C2 mid-exam (M9-I-09).
    expect((await students[0].login(students[0].hwid, 'R1C2')).status()).toBe(200);
    const tile = await waitForTile(page, students[0], users[0].name);
    await expect(tile).toContainText(/seat mismatch: logged in from R1C2/i);
    await expect(tile.getByTestId('seat-mismatch')).toBeVisible();
    await expect(tile.getByTestId('tile-status')).toHaveText('Critical');
  });
});
