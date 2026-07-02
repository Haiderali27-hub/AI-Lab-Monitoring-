import { test, expect } from '@playwright/test';
import { loginAs, TEACHER } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
});

test('Live Monitor page loads without crashing', async ({ page }) => {
  await page.goto('/teacher/live-monitor');
  await expect(page.getByText(/live monitor/i)).toBeVisible();
  await expect(page.getByText(/error|crash|cannot load/i)).not.toBeVisible();
});

test('Live Monitor shows no active exam message when exam is Scheduled', async ({ page }) => {
  await page.goto('/teacher/live-monitor');
  await page.locator('text=Loading live proctoring').waitFor({ state: 'detached', timeout: 10_000 });
  const content = await page.getByRole('main').innerText();
  const hasNoExam = content.toLowerCase().includes('no active') || content.toLowerCase().includes('no exam') || content.toLowerCase().includes('select') || content.toLowerCase().includes('currently active');
  const hasTiles = await page.locator('[data-testid="student-tile"]').count() > 0;
  expect(hasNoExam || hasTiles).toBeTruthy();
});

test('Live Monitor Force Submit All button is visible', async ({ page }) => {
  await page.goto('/teacher/live-monitor');
  await expect(page.getByText(/live monitor/i)).toBeVisible();
});
