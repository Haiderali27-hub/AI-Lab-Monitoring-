import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER, STUDENT } from './helpers';
import { LoginPage } from './pages/LoginPage';

test('Admin login redirects to admin dashboard', async ({ page }) => {
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

test('Wrong password shows error message', async ({ page }) => {
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
