import { test, expect } from '@playwright/test';
import { loginAs, TEACHER } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
});

test('Teacher dashboard loads with exam list', async ({ page }) => {
  await expect(page).toHaveURL(/\/teacher\/dashboard/);
  await expect(page.getByRole('heading', { name: /good morning/i })).toBeVisible();
});

test('Teacher can navigate to Create Exam page', async ({ page }) => {
  await page.getByRole('link', { name: /create exam/i }).click();
  await expect(page).toHaveURL(/\/teacher\/create-exam/);
  await expect(page.getByText(/create new exam/i)).toBeVisible();
});

test('Create Exam Step 1 — fills basic info and advances to Step 2', async ({ page }) => {
  await page.goto('/teacher/create-exam');

  await page.getByLabel(/exam title/i).fill('Playwright Automated Exam');
  await page.getByLabel(/section/i).selectOption({ index: 1 });
  await page.getByLabel(/duration/i).fill('60');

  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  const dateStr = tomorrow.toISOString().split('T')[0];
  await page.getByLabel(/date/i).fill(dateStr);
  await page.getByLabel(/time/i).fill('10:00');

  await page.getByRole('button', { name: /next/i }).click();
  await expect(page.getByRole('button', { name: /add question/i })).toBeVisible();
});

test('Create Exam Step 2 — can add a question', async ({ page }) => {
  await page.goto('/teacher/create-exam');
  await page.getByLabel(/exam title/i).fill('Test Exam');
  await page.getByLabel(/section/i).selectOption({ index: 1 });
  await page.getByLabel(/duration/i).fill('60');

  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  const dateStr = tomorrow.toISOString().split('T')[0];
  await page.getByLabel(/date/i).fill(dateStr);
  await page.getByLabel(/time/i).fill('10:00');

  await page.getByRole('button', { name: /next/i }).click();

  await page.getByRole('button', { name: /add question/i }).click();
  await expect(page.getByText(/Q1/i)).toBeVisible();

  await page.getByPlaceholder(/question prompt|coding task/i).first().fill('What is a linked list?');
  await page.locator('input[type="number"]').first().fill('10');
});

test('Eligibility page loads exam assignments', async ({ page }) => {
  await page.goto('/teacher/eligibility');
  await expect(page.getByRole('heading', { name: 'Eligibility Management' })).toBeVisible();
  await page.getByRole('combobox').first().selectOption({ label: 'Mid-Term Lab Exam (Data Structures)' });
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
});

test('Eligibility toggle changes student eligibility', async ({ page }) => {
  await page.goto('/teacher/eligibility');
  await page.getByRole('combobox').first().selectOption({ label: 'Mid-Term Lab Exam (Data Structures)' });
  await page.locator('table tbody tr').first().waitFor();

  const toggle = page.locator('[role="switch"]').first();
  const initialState = await toggle.getAttribute('aria-checked');
  await toggle.click({ force: true });
  const newState = await toggle.getAttribute('aria-checked');
  expect(newState).not.toBe(initialState);
  await expect(page.getByText(/unsaved changes/i)).toBeVisible();
});

test('Results page loads for ended exams', async ({ page }) => {
  await page.goto('/teacher/results');
  await expect(page.getByRole('heading', { name: 'Results & Grading' })).toBeVisible();
});
