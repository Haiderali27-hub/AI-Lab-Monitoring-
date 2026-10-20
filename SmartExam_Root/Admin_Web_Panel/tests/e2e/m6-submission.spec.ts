import { test, expect } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import { acceptDialogs, examWithStudents, signInStudentWeb, signInWeb } from '../support/scenario';

test.describe.configure({ timeout: 60_000 });

const THEORY_ANSWER = 'A stack is LIFO and a queue is FIFO.';

test.describe('M6 — Submission & Backup (teacher side)', () => {
  test('M6-E-01 Results show submitted student', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);
    const mcq = exam.questions.find(q => q.type === 'Mcq')!;
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    await student.saveAnswer(mcq.questionId, '1');              // correct → 5/5
    await student.saveAnswer(theory.questionId, THEORY_ANSWER); // 4/4 keywords → 0.9 × 10 = 9
    expect((await student.submit()).status()).toBe(200);

    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    await expect(page.getByRole('heading', { name: exam.title })).toBeVisible();
    await expect(page.getByText('1 of 1 Students Submitted')).toBeVisible();
    const row = page.locator('tbody tr').filter({ hasText: users[0].name });
    await expect(row).toContainText('Submitted');
    await expect(row).toContainText('14/15');
  });

  test('M6-E-02 Force submit all from UI', async ({ page, request }) => {
    await (await StaffApi.teacher(request)).endAllActiveExams();
    const { students, exam, teacher } = await examWithStudents(request, 2);
    for (const s of students) await s.start(exam.examId);
    const q = exam.questions[1];
    expect((await students[0].saveAnswer(q.questionId, 'draft')).status()).toBe(200);

    await signInWeb(page, teacher);
    await page.goto('/teacher/live-monitor');
    await expect(page.getByText(`Live Monitor — ${exam.title}`)).toBeVisible();
    const dialogs = acceptDialogs(page);
    await page.getByRole('button', { name: /force end exam/i }).click();
    await expect(page).toHaveURL(/\/teacher\/dashboard/);
    expect(dialogs.join('\n')).toContain('force-submit all student sessions');

    // The simulator's next save is rejected and every session is locked
    const late = await students[0].saveAnswer(q.questionId, 'after force submit');
    expect(late.status()).toBe(400);
    const results = await teacher.results(exam.examId);
    expect(results.map(r => r.status)).toEqual(['ForceSubmitted', 'ForceSubmitted']);
    expect(results.find(r => r.answers.length)!.answers[0].answerText).toBe('draft');
  });

  test('M6-E-03 Per-student force submit button', async ({ page, request }) => {
    await (await StaffApi.teacher(request)).endAllActiveExams();
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    await students[0].start(exam.examId);
    await signInWeb(page, teacher);
    await page.goto('/teacher/live-monitor');
    const tile = page.getByTestId('student-tile').filter({ hasText: users[0].name });
    await expect(async () => { await students[0].heartbeat(); await expect(tile).toBeVisible({ timeout: 1_000 }); }).toPass();
    acceptDialogs(page);
    await tile.click();
    await page.getByRole('button', { name: /force submit student/i }).click();
    await expect(tile.getByTestId('tile-status')).toHaveText(/submitted/i);
    // The session is locked: the drawer no longer offers actions, and the server agrees.
    await expect(page.getByTestId('drawer-finalised')).toContainText(/force submitted/i);
    expect((await students[0].saveAnswer(exam.questions[1].questionId, 'after force')).status()).toBe(400);
    const [row] = await (await StaffApi.teacher(request)).results(exam.examId);
    expect(row.status).toBe('ForceSubmitted');
    expect(row.receiptSha256).toMatch(/^[0-9a-f]{64}$/);
  });

  test('M6-E-06 Per-student extra time from the drawer', async ({ page, request }) => {
    await (await StaffApi.teacher(request)).endAllActiveExams();
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    await students[0].start(exam.examId);
    const before = (await (await students[0].heartbeat()).json()).data.remainingSeconds as number;

    await signInWeb(page, teacher);
    await page.goto('/teacher/live-monitor');
    const tile = page.getByTestId('student-tile').filter({ hasText: users[0].name });
    await expect(async () => { await students[0].heartbeat(); await expect(tile).toBeVisible({ timeout: 1_000 }); }).toPass();
    acceptDialogs(page, '10');
    await tile.click();
    await page.getByTestId('extend-student-time').click();
    await expect(tile.getByTestId('tile-extra-time')).toHaveText('+10 min');

    // Only this student's clock moved (server-side, per session).
    const ack = (await (await students[0].heartbeat()).json()).data;
    expect(ack.extraMinutes).toBe(10);
    expect(ack.remainingSeconds - before).toBeGreaterThanOrEqual(10 * 60 - 30);
  });

  test('M6-E-04 Receipt hash on results page', async ({ page, request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    await students[0].start(exam.examId);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    expect((await students[0].saveAnswer(theory.questionId, THEORY_ANSWER)).status()).toBe(200);
    const receipt = await (await students[0].submit()).json(); // { message, submittedAt, answerCount, sha256 } (M6-I-02)
    expect(receipt.answerCount).toBe(1);
    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    await expect(page.getByTestId('receipt-hash')).toHaveText(receipt.sha256);
  });

  test('M6-E-05 Student result page shows the same receipt', async ({ page, request }) => {
    const { users, students, exam, teacher } = await examWithStudents(request, 1);
    await students[0].start(exam.examId);
    const mcq = exam.questions.find(q => q.type === 'Mcq')!;
    expect((await students[0].saveAnswer(mcq.questionId, '1')).status()).toBe(200);
    const receipt = await (await students[0].submit()).json();
    expect((await teacher.forceSubmitAll(exam.examId)).status()).toBe(200); // ends the exam → results released

    await signInStudentWeb(page, request, users[0]);
    await page.goto(`/student/exams/${exam.examId}/result`);
    await expect(page.getByTestId('student-receipt-hash')).toHaveText(receipt.sha256);
  });
});
