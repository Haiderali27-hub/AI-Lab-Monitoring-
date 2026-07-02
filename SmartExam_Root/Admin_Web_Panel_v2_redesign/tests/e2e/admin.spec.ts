import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, waitForToast } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
});

test('Admin dashboard shows stat cards with numbers', async ({ page }) => {
  await expect(page).toHaveURL(/\/admin\/dashboard/);
  await expect(page.getByText(/total students/i)).toBeVisible();
  await expect(page.getByText(/active exams/i)).toBeVisible();
});

test('Admin can navigate to User Management via sidebar', async ({ page }) => {
  await page.getByRole('link', { name: /users/i }).click();
  await expect(page).toHaveURL(/\/admin\/users/);
  await expect(page.getByText(/user management/i)).toBeVisible();
});

test('User Management table loads and shows users', async ({ page }) => {
  await page.goto('/admin/users');
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
  const rowCount = await page.locator('table tbody tr').count();
  expect(rowCount).toBeGreaterThanOrEqual(4);
});

test('Admin can search for a user by name', async ({ page }) => {
  await page.goto('/admin/users');
  await page.getByPlaceholder(/search/i).fill('Ali');
  await page.waitForTimeout(300); // debounce
  const rows = page.locator('table tbody tr');
  const count = await rows.count();
  for (let i = 0; i < count; i++) {
    await expect(rows.nth(i)).toContainText(/ali/i);
  }
});

test('Admin can open Add User panel and create a user', async ({ page }) => {
  await page.goto('/admin/users');
  await page.getByRole('button', { name: /add user/i }).click();
  await expect(page.getByText(/add new user/i)).toBeVisible();

  await page.getByLabel(/full name/i).fill('Playwright Test User');
  await page.getByLabel(/email/i).fill(`playwright_${Date.now()}@test.com`);
  await page.getByLabel(/password/i).fill('Test@12345');
  await page.getByLabel(/role/i).selectOption('Student');

  await page.getByRole('button', { name: /create user/i }).click();
  await expect(page.getByText(/add new user/i)).not.toBeVisible({ timeout: 5_000 });
});

test('Tab filter shows only students when Students tab clicked', async ({ page }) => {
  await page.goto('/admin/users');
  await page.getByRole('tab', { name: /students/i }).click();
  await page.waitForTimeout(300);

  const roleBadges = page.locator('[data-testid="role-badge"]');
  const count = await roleBadges.count();
  for (let i = 0; i < count; i++) {
    await expect(roleBadges.nth(i)).toContainText('Student');
  }
});

test('Device Bindings page loads and shows students', async ({ page }) => {
  await page.goto('/admin/device-bindings');
  await expect(page.getByRole('heading', { name: 'Device Bindings' })).toBeVisible();
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
});

test('Reset binding shows confirmation modal before proceeding', async ({ page }) => {
  await page.goto('/admin/device-bindings');
  await page.getByRole('button', { name: /reset binding/i }).first().click();
  await expect(page.getByText(/are you sure|reset device binding/i)).toBeVisible();
  await page.getByRole('button', { name: /cancel/i }).click();
  await expect(page.getByText(/are you sure|reset device binding/i)).not.toBeVisible();
});
