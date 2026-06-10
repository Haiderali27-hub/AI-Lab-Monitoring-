import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER } from './helpers';

test('Admin System Analytics page loads with stat cards', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/analytics');

  await expect(page.getByText(/system analytics/i)).toBeVisible();
  await expect(page.getByText(/total students/i)).toBeVisible();
  await expect(page.getByText(/total teachers/i)).toBeVisible();
  await expect(page.getByText(/total exams/i)).toBeVisible();
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
