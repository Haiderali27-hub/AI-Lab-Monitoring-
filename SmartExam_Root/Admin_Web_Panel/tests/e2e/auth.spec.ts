import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER, STUDENT } from './helpers';
import { LoginPage } from './pages/LoginPage';
import { API_URL } from '../support/env';

test('M1-E-01 Login page — valid admin lands on /admin/dashboard', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await expect(page).toHaveURL(/\/admin\/dashboard/);
  await expect(page.getByText(/overview/i)).toBeVisible();
});

test('Teacher login redirects to teacher dashboard', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await expect(page).toHaveURL(/\/teacher\/dashboard/);
});

test('Student login redirects to student portal', async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
  await expect(page).toHaveURL(/\/student\/dashboard/);
});

test('M1-E-02 Login page — invalid creds shows error and stays on /login', async ({ page }) => {
  const loginPage = new LoginPage(page);
  await loginPage.loginAs(ADMIN.email, 'WrongPassword123!');
  await expect(page.getByText(/invalid email or password/i)).toBeVisible();
  await expect(page).toHaveURL(/\/login/); // stayed on login
});

test('Empty form shows validation before submitting', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/login/);
});

test('Protected route redirects to login when not authenticated', async ({ page }) => {
  await page.goto('/admin/dashboard');
  await expect(page).toHaveURL(/\/login/);
});

test('Student cannot access admin dashboard', async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
  await page.goto('/admin/dashboard');
  await expect(page).not.toHaveURL(/\/admin\/dashboard/);
});

test('Logout clears session and redirects to login', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await expect(page).toHaveURL(/\/admin\/dashboard/);
  await page.getByRole('button', { name: /logout/i }).click();
  await expect(page).toHaveURL(/\/login/);
  await page.goto('/admin/dashboard');
  await expect(page).toHaveURL(/\/login/);
});

// ── Module 1: refresh tokens in the web panel (src/api/client.ts interceptor) ──

const JWT = /^[\w-]+\.[\w-]+\.[\w-]+$/;

test('M1-E-03 Expired access token is refreshed transparently', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  const refreshBefore = await page.evaluate(() => localStorage.getItem('smartexam_refresh_token'));
  expect(refreshBefore).toBeTruthy();

  // Simulate an expired / rejected access token; the refresh token is still valid.
  await page.evaluate(() => localStorage.setItem('smartexam_token', 'expired.invalid.token'));
  await page.goto('/admin/users');

  // The 401 is answered with one refresh + retry, so the page just works.
  await expect(page.locator('table tbody tr').filter({ hasText: 'ali@smartexam.com' })).toHaveCount(1, { timeout: 10_000 });
  await expect(page).toHaveURL(/\/admin\/users/);
  const after = await page.evaluate(() => ({
    token: localStorage.getItem('smartexam_token'),
    refresh: localStorage.getItem('smartexam_refresh_token'),
  }));
  expect(after.token).toMatch(JWT);
  expect(after.refresh).toBeTruthy();
  expect(after.refresh).not.toBe(refreshBefore); // rotated
});

test('M1-E-04 Rejected refresh token sends the user back to login', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.evaluate(() => {
    localStorage.setItem('smartexam_token', 'expired.invalid.token');
    localStorage.setItem('smartexam_refresh_token', 'revoked-or-unknown');
  });
  await page.goto('/admin/users');
  await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  expect(await page.evaluate(() => localStorage.getItem('smartexam_refresh_token'))).toBeNull();
});

test('M1-E-05 Logout revokes the refresh token server-side', async ({ page, request }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  const refreshToken = await page.evaluate(() => localStorage.getItem('smartexam_refresh_token'));
  expect(refreshToken).toBeTruthy();

  await page.getByRole('button', { name: /logout/i }).click();
  await expect(page).toHaveURL(/\/login/);

  const res = await request.post(`${API_URL}/api/auth/refresh`, { data: { refreshToken } });
  expect(res.status()).toBe(401);
});
