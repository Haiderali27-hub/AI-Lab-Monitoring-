import { test, expect } from '@playwright/test';
import { loginAs, ADMIN } from './helpers';
import { API_URL } from '../support/env';
import { StaffApi } from '../support/staffApi';
import { StudentClient } from '../support/studentClient';

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

test('M7-E-01 Users page lists users', async ({ page }) => {
  await page.goto('/admin/users');
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
  const rowCount = await page.locator('table tbody tr').count();
  expect(rowCount).toBeGreaterThanOrEqual(4);
  for (const email of [ADMIN.email, 'teacher@smartexam.com', 'ali@smartexam.com', 'sara@smartexam.com']) {
    await expect(page.locator('table tbody tr').filter({ hasText: email })).toHaveCount(1);
  }
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

test('M7-E-02 Create user via UI', async ({ page }) => {
  const email = `playwright_${Date.now()}@test.com`;
  await page.goto('/admin/users');
  await page.getByRole('button', { name: /add user/i }).click();
  await expect(page.getByText(/add new user/i)).toBeVisible();

  await page.getByLabel(/full name/i).fill('Playwright Test User');
  await page.getByLabel(/email/i).fill(email);
  await page.getByLabel(/password/i).fill('Test@12345');
  await page.getByLabel(/role/i).selectOption('Student');

  await page.getByRole('button', { name: /create user/i }).click();
  await expect(page.getByText(/add new user/i)).not.toBeVisible({ timeout: 5_000 });

  // The new row is in the table
  await page.getByPlaceholder(/search/i).fill(email);
  await expect(page.locator('table tbody tr').filter({ hasText: email })).toHaveCount(1);
});

test('M1-E-06 Admin sets a student roll number, which works as a login identifier', async ({ page, request }) => {
  const email = `roll_${Date.now()}@test.com`;
  const roll = `BSCS-PW-${Date.now().toString().slice(-6)}`;
  await page.goto('/admin/users');
  await page.getByRole('button', { name: /add user/i }).click();
  await page.getByLabel(/full name/i).fill('Roll Number Student');
  await page.getByLabel(/email/i).fill(email);
  await page.getByLabel(/password/i).fill('Test@12345');
  await page.getByLabel(/role/i).selectOption('Student');
  await page.getByLabel(/roll number/i).fill(roll.toLowerCase());
  await page.getByRole('button', { name: /create user/i }).click();
  await expect(page.getByText(/add new user/i)).not.toBeVisible({ timeout: 5_000 });

  // Stored normalised (upper-case) and searchable.
  await page.getByPlaceholder(/search/i).fill(roll);
  const row = page.locator('table tbody tr').filter({ hasText: email });
  await expect(row).toHaveCount(1);
  await expect(row.getByTestId('user-roll-number')).toHaveText(roll);

  const login = await StudentClient.rawLogin(request, { usernameOrEmail: roll, password: 'Test@12345', hardwareFingerprint: `HWID-${roll}` });
  expect(login.status()).toBe(200);
});

test('M7-E-03 Audit log shows login', async ({ page }) => {
  await page.goto('/admin/audit-logs');
  await expect(page.getByRole('heading', { name: /security & administrative logs/i })).toBeVisible();
  const loginRows = page.locator('table tbody tr').filter({ hasText: 'LOGIN_SUCCESS' }).filter({ hasText: 'Admin User' });
  await expect(loginRows.first()).toBeVisible({ timeout: 8_000 });
});

test.fixme('M7-E-04 Invigilator UI route', async ({ page, request }) => {
  // Invigilator role has no web UI yet (App.tsx only routes Admin/Teacher/Student).
  const tag = Date.now();
  const adminLogin = await (await request.post(`${API_URL}/api/auth/login`, { data: ADMIN })).json();
  await request.post(`${API_URL}/api/users`, {
    headers: { Authorization: `Bearer ${adminLogin.token}` },
    data: { name: `Invigilator ${tag}`, email: `inv_${tag}@test.com`, password: 'Invig@123', role: 'Invigilator' },
  });
  await page.context().clearCookies();
  await page.evaluate(() => localStorage.clear());
  await loginAs(page, `inv_${tag}@test.com`, 'Invig@123');
  await expect(page).toHaveURL(/\/invigilator\/live-monitor/);
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

test('Reset binding shows confirmation modal before proceeding', async ({ page, request }) => {
  // Own bound student (fresh E2E databases have no bindings until a desktop client logs in)
  const user = await (await StaffApi.admin(request)).createStudent('Bound');
  await new StudentClient(request, user.email, user.password).signIn();

  await page.goto('/admin/device-bindings');
  await page.getByPlaceholder(/search by student/i).fill(user.email);
  await page.getByRole('button', { name: /reset binding/i }).first().click();
  await expect(page.getByText(/are you sure|reset device binding/i)).toBeVisible();
  await page.getByRole('button', { name: /cancel/i }).click();
  await expect(page.getByText(/are you sure|reset device binding/i)).not.toBeVisible();
});
