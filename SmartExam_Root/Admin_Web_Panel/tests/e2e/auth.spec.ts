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

test('M1-E-07 Logout after the access token expired still revokes the session', async ({ page, request }) => {
  // Verifier regression: logout skipped the proactive refresh, so after an idle spell it was sent with an
  // expired token, got 401, and the refresh-token family stayed alive server-side.
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.evaluate(() => {
    const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
    const expired = `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64({ exp: Math.floor(Date.now() / 1000) - 120 })}.sig`;
    localStorage.setItem('smartexam_token', expired);
  });

  // Capture the rotated pair through a pass-through route: the page navigates to /login right after the
  // logout, after which the browser no longer has the refresh response body.
  let newRefreshToken: string | null = null;
  await page.route('**/api/auth/refresh', async route => {
    const response = await route.fetch();
    if (response.status() === 200) newRefreshToken = (await response.json()).data.refreshToken;
    await route.fulfill({ response });
  });
  const loggedOut = page.waitForResponse(r => r.url().includes('/api/auth/logout'));
  await page.getByRole('button', { name: /logout/i }).click();

  expect((await loggedOut).status()).toBe(200);
  await expect(page).toHaveURL(/\/login/);
  expect(newRefreshToken).toBeTruthy();

  // The newest refresh token was revoked by the logout (not by reuse detection).
  const res = await request.post(`${API_URL}/api/auth/refresh`, { data: { refreshToken: newRefreshToken } });
  expect(res.status()).toBe(401);
  expect((await res.json()).code).toBe('INVALID_REFRESH_TOKEN');
});
