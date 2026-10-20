import { test, expect } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import { examWithStudents, signInStudentWeb, signInWeb } from '../support/scenario';

// Module 2 — Pre-Exam Verification & Mock Test. The student simulator posts what the desktop
// "Run check" button sends (POST /api/exams/{id}/readiness); the teacher checks it in the browser.

test.describe('M2 — Pre-Exam Verification & Mock Test', () => {
  test('M2-E-01 Teacher sees readiness column', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 2, { requireReadiness: true });
    expect((await students[0].readiness(exam.examId)).status()).toBe(200);

    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);
    await expect(page.getByRole('columnheader', { name: /system check/i })).toBeVisible();
    const passed = page.locator('tbody tr').filter({ hasText: users[0].name });
    await expect(passed.getByTestId('readiness-status')).toHaveText('✓');
    // The second student never ran the check
    const notRun = page.locator('tbody tr').filter({ hasText: users[1].name });
    await expect(notRun.getByTestId('readiness-status')).toHaveText('—');
  });

  test('M2-E-02 Failed readiness visible', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1, { requireReadiness: true, requireWebcam: true });
    expect((await students[0].readiness(exam.examId, [{ name: 'Camera', passed: false, detail: 'No device found' }])).status()).toBe(200);

    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);
    const status = page.locator('tbody tr').filter({ hasText: users[0].name }).getByTestId('readiness-status');
    await expect(status).toHaveText('✗');
    await status.hover();
    await expect(page.getByRole('tooltip')).toContainText('Camera: No device found');

    // ...and the server keeps the start locked
    expect((await students[0].startSession(exam.examId)).status()).toBe(400);
  });

  test('M2-E-03 Create exam: readiness toggles', async ({ page, request }) => {
    const teacher = await StaffApi.teacher(request);
    await signInWeb(page, teacher);
    const title = `Readiness Exam ${Date.now()}`;
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
    await page.getByPlaceholder(/question prompt|coding task/i).first().fill('Explain a queue.');
    await page.getByRole('button', { name: /next step/i }).click();

    // Step 3 — readiness on by default for new exams; add webcam
    await expect(page.getByLabel(/require pre-exam system check/i)).toBeChecked();
    await expect(page.getByLabel(/require webcam/i)).not.toBeChecked();
    await page.getByLabel(/require webcam/i).check();
    await page.getByRole('button', { name: /next step/i }).click();
    await page.getByRole('button', { name: 'Create Exam' }).click();
    await expect(page).toHaveURL(/\/teacher\/dashboard/);

    const created = (await teacher.exams()).find((e: any) => e.title === title);
    expect(created, 'exam created via the wizard').toBeTruthy();
    const detail = await teacher.examDetail(created.examId);
    expect(detail.requireReadiness).toBe(true);
    expect(detail.requireWebcam).toBe(true);
    expect(detail.isMock).toBe(false);

    // Edit page shows the saved values
    await page.goto(`/teacher/exams/${created.examId}/edit`);
    await page.getByRole('button', { name: /next step/i }).click();
    await page.getByRole('button', { name: /next step/i }).click();
    await expect(page.getByLabel(/require webcam/i)).toBeChecked();
    await expect(page.getByLabel(/practice exam/i)).not.toBeChecked();
  });

  test('M2-E-04 Practice exam labelled', async ({ page, request }) => {
    const title = `Mock ${Date.now()}`;
    const { users } = await examWithStudents(request, 1, { title, isMock: true });
    await signInStudentWeb(page, request, users[0]);
    await page.goto('/student/exams');
    const card = page.locator('div').filter({ hasText: title }).filter({ has: page.getByTestId('practice-badge') }).last();
    await expect(card.getByTestId('practice-badge')).toHaveText(/practice/i);
  });
});
