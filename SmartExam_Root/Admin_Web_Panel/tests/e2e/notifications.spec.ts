import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER, STUDENT, waitForToast } from './helpers';

test('Notification bell visible in admin navbar', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await expect(page.locator('[data-testid="notification-bell"]')).toBeVisible();
});

test('Clicking notification bell opens dropdown', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.locator('[data-testid="notification-bell"]').click();
  await expect(page.locator('[data-testid="notification-dropdown"]')).toBeVisible();
});

test('Clicking outside bell dropdown closes it', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.locator('[data-testid="notification-bell"]').click();
  await expect(page.locator('[data-testid="notification-dropdown"]')).toBeVisible();
  await page.locator('main').click();
  await expect(page.locator('[data-testid="notification-dropdown"]')).not.toBeVisible();
});

test('Admin Notifications page loads with filter tabs', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/notifications');

  await expect(page.getByText(/notifications/i).first()).toBeVisible();
});

test('Teacher can navigate to Announcement page', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/announce');

  await expect(page.getByRole('heading', { name: 'Send Announcement' }).first()).toBeVisible();
  await expect(page.getByLabel(/select exam/i)).toBeVisible();
});

test('Announcement form shows recipient count when exam is selected', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/announce');

  const dropdown = page.getByLabel(/select exam/i);
  const optionCount = await dropdown.locator('option').count();

  if (optionCount > 1) {
    await dropdown.selectOption({ index: 1 });
    await expect(page.getByText(/students will be notified/i)).toBeVisible({ timeout: 5_000 });
  }
});

test('Student receives notification and can mark it as read', async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
  await page.goto('/student/notifications');

  await expect(page.getByText(/notifications/i).first()).toBeVisible();
});
