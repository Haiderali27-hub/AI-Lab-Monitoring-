import { test, expect } from '@playwright/test';
import { acceptDialogs, examWithStudents, signInWeb } from '../support/scenario';

test.describe('M9 — Eligibility & Access Control (teacher UI)', () => {
  test('M9-E-01 Eligibility page loads', async ({ page, request }) => {
    const { users, exam, teacher } = await examWithStudents(request, 2);
    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);

    await expect(page.getByRole('heading', { name: 'Eligibility Management' })).toBeVisible();
    await expect(page.getByText('2 Students')).toBeVisible();
    for (const u of users) {
      const row = page.locator('tbody tr').filter({ hasText: u.name });
      await expect(row).toContainText(u.email);
      await expect(row.getByRole('switch')).toBeChecked();
    }
  });

  test('M9-E-02 Toggle eligibility in UI', async ({ page, request }) => {
    const { users, students, exam, teacher } = await examWithStudents(request, 2);
    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);

    const row = page.locator('tbody tr').filter({ hasText: users[0].name });
    await row.getByRole('switch').setChecked(false, { force: true });
    await row.getByPlaceholder('Add reason/note...').fill('Fee defaulter');
    await expect(page.getByText(/unsaved changes/i)).toBeVisible();

    const dialogs = acceptDialogs(page);
    await page.getByRole('button', { name: 'Save Changes' }).click();
    await expect.poll(() => dialogs.join('\n')).toContain('successfully updated');

    // The ineligible student's desktop client can no longer start; the other still can
    const blocked = await students[0].startSession(exam.examId);
    expect(blocked.status()).toBe(400);
    expect((await students[0].currentExam()).message).toBe('Fee defaulter');
    expect((await students[1].startSession(exam.examId)).status()).toBe(200);
  });

  test.fixme('M9-E-03 Attendance upload dialog', async ({ page, request }) => {
    const { users, exam, teacher } = await examWithStudents(request, 2);
    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);
    await page.getByRole('button', { name: /upload attendance/i }).click();
    await page.getByLabel(/csv file/i).setInputFiles({
      name: 'attendance.csv', mimeType: 'text/csv',
      buffer: Buffer.from(`email,attended,total\n${users[0].email},30,40\n${users[1].email},24,40\n`),
    });
    await page.getByRole('button', { name: /import/i }).click();
    await expect(page.locator('tbody tr').filter({ hasText: users[0].name })).toContainText('75%');
  });

  test.fixme('M9-E-04 Recalculate button', async ({ page, request }) => {
    const { users, exam, teacher } = await examWithStudents(request, 1);
    // precondition: attendance 60% imported for users[0] (M9-I-01), MinAttendance 75
    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);
    await page.getByRole('button', { name: /recalculate/i }).click();
    const row = page.locator('tbody tr').filter({ hasText: users[0].name });
    await expect(row.getByRole('switch')).not.toBeChecked();
    await expect(row).toContainText('Attendance 60% < 75%');
  });
});
