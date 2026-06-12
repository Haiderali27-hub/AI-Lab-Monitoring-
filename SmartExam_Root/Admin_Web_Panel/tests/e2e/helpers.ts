import { Page, expect } from '@playwright/test';

// ── Credentials (match your backend seed data) ─────────────────────────────
export const ADMIN    = { email: 'admin@smartexam.com',   password: 'Admin@123' };
export const TEACHER  = { email: 'teacher@smartexam.com', password: 'Teacher@123' };
export const STUDENT  = { email: 'ali@smartexam.com',     password: 'Student@123' };
export const STUDENT2 = { email: 'sara@smartexam.com',    password: 'Student@123' };

// ── Login helper ──────────────────────────────────────────────────────────
export async function loginAs(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel(/email/i).fill(email);
  await page.getByLabel(/password/i).fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  // Wait for redirect away from login page
  await page.waitForURL(url => !url.pathname.includes('/login'), { timeout: 10_000 });
}

// ── Wait for toast helper ─────────────────────────────────────────────────
export async function waitForToast(page: Page, text: string) {
  await expect(page.getByText(text)).toBeVisible({ timeout: 8_000 });
}

// ── Check page title helper ───────────────────────────────────────────────
export async function expectPageTitle(page: Page, title: string) {
  await expect(page.getByRole('heading', { name: title })).toBeVisible();
}
