import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { loginAs, TEACHER } from './helpers';
import { StaffApi } from '../support/staffApi';

// Eligibility dropdown label for the seeded demo exam: "<title> (<course>)"
const SEEDED_EXAM_OPTION = 'Mid-Term Lab Exam (Demo) (Data Structures)';

test.beforeEach(async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
});

test('Teacher dashboard loads with exam list', async ({ page }) => {
  await expect(page).toHaveURL(/\/teacher\/dashboard/);
  await expect(page.getByRole('heading', { name: /good (morning|afternoon|evening)/i })).toBeVisible();
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
  await expect(page.getByRole('button', { name: /add question/i }).first()).toBeVisible();
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

  await page.getByRole('button', { name: /add question/i }).first().click();
  await expect(page.getByText(/Q1/i)).toBeVisible();

  await page.getByPlaceholder(/question prompt|coding task/i).first().fill('What is a linked list?');
  await page.locator('input[type="number"]').first().fill('10');
});

test('Eligibility page loads exam assignments', async ({ page }) => {
  await page.goto('/teacher/eligibility');
  await expect(page.getByRole('heading', { name: 'Eligibility Management' })).toBeVisible();
  await page.getByRole('combobox').first().selectOption({ label: SEEDED_EXAM_OPTION });
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
});

test('Eligibility toggle changes student eligibility', async ({ page }) => {
  await page.goto('/teacher/eligibility');
  await page.getByRole('combobox').first().selectOption({ label: SEEDED_EXAM_OPTION });
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

test('M8-E-01 Create exam wizard end-to-end', async ({ page }) => {
  const title = `Wizard Exam ${Date.now()}`;
  await page.goto('/teacher/create-exam');

  // Step 1 — details
  await page.getByLabel(/exam title/i).fill(title);
  await page.getByLabel(/section/i).selectOption({ index: 1 });
  const tomorrow = new Date(Date.now() + 24 * 3600_000).toISOString().split('T')[0];
  await page.getByLabel(/date/i).fill(tomorrow);
  await page.getByLabel(/time/i).fill('10:00');
  await page.getByRole('button', { name: /next step/i }).click();

  // Step 2 — two questions: theory + MCQ
  await page.getByRole('button', { name: /add question/i }).first().click();
  await page.getByRole('button', { name: /add question/i }).first().click();
  const typeSelects = page.locator('select').filter({ has: page.locator('option[value="Mcq"]') });
  const prompts = page.getByPlaceholder(/question prompt|coding task/i);
  await typeSelects.nth(0).selectOption('Theory');
  await prompts.nth(0).fill('Explain recursion with an example.');
  await typeSelects.nth(1).selectOption('Mcq');
  await prompts.nth(1).fill('Which traversal visits the root first?');
  await page.getByPlaceholder('Option A').fill('Preorder');
  await page.getByPlaceholder('Option B').fill('Inorder');
  await page.getByTitle('Mark as correct answer').first().check();
  await page.getByRole('button', { name: /next step/i }).click();

  // Step 3 — rules (defaults) → Step 4 — students (all enrolled pre-selected)
  await expect(page.getByText(/rules & integrity settings/i)).toBeVisible();
  await page.getByRole('button', { name: /next step/i }).click();
  await expect(page.getByText(/assign enrolled students/i)).toBeVisible();
  await page.getByRole('button', { name: 'Create Exam' }).click();

  // Lands on the dashboard with the new exam in the scheduled list
  await expect(page).toHaveURL(/\/teacher\/dashboard/);
  const card = page.locator('h5', { hasText: title });
  await expect(card).toBeVisible();
});

// Fills wizard steps 1–2 (details + one theory question) and stops on step 3.
async function wizardToRulesStep(page: Page, title: string) {
  await page.goto('/teacher/create-exam');
  await page.getByLabel(/exam title/i).fill(title);
  await page.getByLabel(/section/i).selectOption({ index: 1 });
  const tomorrow = new Date(Date.now() + 24 * 3600_000).toISOString().split('T')[0];
  await page.getByLabel(/date/i).fill(tomorrow);
  await page.getByLabel(/time/i).fill('10:00');
  await page.getByRole('button', { name: /next step/i }).click();

  await page.getByRole('button', { name: /add question/i }).first().click();
  const typeSelects = page.locator('select').filter({ has: page.locator('option[value="Mcq"]') });
  await typeSelects.nth(0).selectOption('Theory');
  await page.getByPlaceholder(/question prompt|coding task/i).first().fill('Explain recursion.');
  await page.getByRole('button', { name: /next step/i }).click();
  await expect(page.getByText(/rules & integrity settings/i)).toBeVisible();
}

// Step 3 → 4 → Create; returns the new exam (looked up by title over the API).
async function finishWizard(page: Page, request: APIRequestContext, title: string): Promise<any> {
  await page.getByRole('button', { name: /next step/i }).click();
  await expect(page.getByText(/assign enrolled students/i)).toBeVisible();
  await page.getByRole('button', { name: 'Create Exam' }).click();
  await expect(page).toHaveURL(/\/teacher\/dashboard/);

  const teacher = await StaffApi.teacher(request);
  const list: any[] = await (await teacher.get('/api/exams')).json();
  const summary = list.find(e => e.title === title);
  expect(summary, `exam "${title}" was created`).toBeTruthy();
  return (await teacher.get(`/api/exams/${summary.examId}`)).json();
}

test('M8-E-02 Monitoring settings step', async ({ page, request }) => {
  const title = `Interval Exam ${Date.now()}`;
  await wizardToRulesStep(page, title);

  await expect(page.getByLabel(/screenshot interval/i)).toHaveValue('60'); // default: random 30–90 s
  await page.getByLabel(/screenshot interval/i).fill('5');
  await page.getByRole('button', { name: /next step/i }).click();
  await expect(page.getByText(/screenshot interval must be between 10 and 600/i)).toBeVisible();

  await page.getByLabel(/screenshot interval/i).fill('30');
  await page.getByLabel(/webcam interval/i).fill('120');
  const exam = await finishWizard(page, request, title);
  expect(exam.screenshotIntervalSeconds).toBe(30);
  expect(exam.webcamIntervalSeconds).toBe(120);

  // Reopen in the editor: step 3 is prefilled.
  await page.goto(`/teacher/exams/${exam.examId}/edit`);
  await expect(page.getByLabel(/exam title/i)).toHaveValue(title);
  await page.getByRole('button', { name: /next step/i }).click();
  await page.getByRole('button', { name: /next step/i }).click();
  await expect(page.getByLabel(/screenshot interval/i)).toHaveValue('30');
  await expect(page.getByLabel(/webcam interval/i)).toHaveValue('120');
});

test('M8-E-03 Allowed apps picker', async ({ page, request }) => {
  const title = `Apps Exam ${Date.now()}`;
  await wizardToRulesStep(page, title);

  // Pick from the catalogue instead of typing exe names.
  await page.getByRole('button', { name: /add allowed app/i }).click();
  await page.getByRole('option', { name: /visual studio code/i }).click();
  await expect(page.getByTestId('allowed-app-chip')).toContainText('code.exe');

  // Free-text fallback, normalised to an .exe name; duplicates are ignored.
  await page.getByLabel(/other executable/i).fill('sublime_text');
  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await page.getByRole('button', { name: /add allowed app/i }).click();
  await expect(page.getByRole('option', { name: /visual studio code/i })).toHaveCount(0); // already picked
  await page.getByRole('option', { name: /code::blocks/i }).click();
  await expect(page.getByTestId('allowed-app-chip')).toHaveCount(3);

  // Remove a chip.
  await page.getByRole('button', { name: 'Remove codeblocks.exe' }).click();
  await expect(page.getByTestId('allowed-app-chip')).toHaveCount(2);

  const exam = await finishWizard(page, request, title);
  expect(JSON.parse(exam.allowedApps)).toEqual(['code.exe', 'sublime_text.exe']);
});
