import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER } from './helpers';
import { StaffApi } from '../support/staffApi';
import { examWithStudents, signInWeb } from '../support/scenario';

test('M12-E-03 System analytics page', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/analytics');

  await expect(page.getByText(/system analytics/i)).toBeVisible();
  await expect(page.getByText(/total students/i)).toBeVisible();
  await expect(page.getByText(/total teachers/i)).toBeVisible();
  await expect(page.getByText(/total exams/i)).toBeVisible();
  await expect(page.getByText(/exams per month/i)).toBeVisible();
});

test('Admin Analytics shows Exams Per Month chart', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/analytics');

  await expect(page.getByText(/exams per month/i)).toBeVisible();
});

test('Teacher Exam Analytics shows empty state when no exam selected', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/analytics');

  await expect(page.getByText(/exam analytics/i)).toBeVisible();
  await expect(page.getByText(/no exam selected|select an exam/i)).toBeVisible();
});

test('Teacher can select exam and see analytics load', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/analytics');

  const dropdown = page.getByRole('combobox').first();
  const optionCount = await dropdown.locator('option').count();

  if (optionCount > 1) {
    await dropdown.selectOption({ index: 1 });
    await expect(page.getByText(/average score/i)).toBeVisible({ timeout: 8_000 });
    await expect(page.getByText(/pass rate/i)).toBeVisible();
  } else {
    await expect(page.getByText(/no exam|select/i)).toBeVisible();
  }
});

test('Reports page loads with report history table', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/reports');

  await expect(page.getByRole('heading', { name: 'Generated Reports' })).toBeVisible();
});

test('M12-E-01 Audit logs page', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/audit-logs');
  const rows = page.locator('table tbody tr');
  await expect(rows.first()).toBeVisible({ timeout: 8_000 });

  // Event-type filter narrows the table to that event only
  await page.locator('select').filter({ has: page.locator('option[value="LOGIN_SUCCESS"]') }).selectOption('LOGIN_SUCCESS');
  await expect(rows.first()).toContainText('LOGIN_SUCCESS');
  const count = await rows.count();
  for (let i = 0; i < Math.min(count, 20); i++) await expect(rows.nth(i)).toContainText('LOGIN_SUCCESS');

  // Free-text search
  await page.getByPlaceholder(/search logs/i).fill('teacher@smartexam.com');
  await expect(rows.first()).toContainText(/teacher@smartexam\.com|Dr\. Ahmed/);
});

test('M12-E-02 Reports page generates PDF', async ({ page, request }) => {
  const { students, exam, teacher } = await examWithStudents(request, 1);
  await students[0].start(exam.examId);
  await students[0].submit();

  await signInWeb(page, teacher);
  await page.goto('/teacher/reports');
  await page.locator('select').selectOption({ label: exam.title });
  await expect(page.getByText(/no reports exist for this exam/i)).toBeVisible();
  await page.getByRole('button', { name: /generate new report/i }).click();

  const download = page.getByRole('button', { name: /download/i });
  await expect(download).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('table tbody tr').first()).toContainText('.pdf');
});

test('M12-E-04 Chain verified badge', async ({ page }) => {
  // GET /api/audit-logs/verify (M12-I-01) surfaced on the audit page.
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/audit-logs');
  await expect(page.getByTestId('audit-chain-status')).toHaveText(/chain verified/i);
});

test('M12-E-05 Evidence ZIP download', async ({ page, request }) => {
  const teacher = await StaffApi.teacher(request);
  await signInWeb(page, teacher);
  await page.goto('/teacher/reports');
  await page.locator('select').selectOption({ index: 1 });
  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: /evidence/i }).click();
  expect((await downloadPromise).suggestedFilename()).toMatch(/\.zip$/);
});
