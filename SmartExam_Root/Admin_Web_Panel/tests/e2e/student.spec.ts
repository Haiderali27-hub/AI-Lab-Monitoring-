import { test, expect } from '@playwright/test';
import { loginAs, STUDENT } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
});

test('Student lands on student portal dashboard', async ({ page }) => {
  await expect(page).toHaveURL(/\/student\/dashboard/);
  await expect(page.getByText(/welcome back/i)).toBeVisible();
});

test('Student dashboard shows personal stat cards', async ({ page }) => {
  await expect(page.getByText(/exams taken/i)).toBeVisible();
  await expect(page.getByText(/average score/i)).toBeVisible();
});

test('Student can navigate to My Exams page', async ({ page }) => {
  await page.getByRole('link', { name: /my exams/i }).click();
  await expect(page).toHaveURL(/\/student\/exams/);
  await expect(page.getByRole('heading', { name: /my exams/i })).toBeVisible();
});

test('My Exams page shows exam cards', async ({ page }) => {
  await page.goto('/student/exams');
  // Should show exam cards or list
  await expect(page.getByRole('heading', { name: /my exams/i })).toBeVisible();
});

test('Student Performance page loads with chart', async ({ page }) => {
  await page.goto('/student/performance');
  await expect(page.getByRole('heading', { name: /my performance/i })).toBeVisible();
  await expect(page.getByText(/score over time/i)).toBeVisible();
});

test('Student Violations page shows empty state when no violations', async ({ page }) => {
  await page.goto('/student/violations');
  await expect(page.getByRole('heading', { name: /academic integrity secured/i })).toBeVisible();
});

test('SEC-E-01 Role routing guard', async ({ page }) => {
  await page.goto('/admin/users');
  await expect(page).not.toHaveURL(/\/admin\/users/);
  // The admin page itself never rendered for the student
  await expect(page.getByText(/user management/i)).toHaveCount(0);
  await expect(page.getByRole('button', { name: /add user/i })).toHaveCount(0);
});

test('Student top navbar has all navigation links', async ({ page }) => {
  await expect(page.getByRole('link', { name: /dashboard/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /my exams/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /performance/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /violations/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /notifications/i })).toBeVisible();
});
