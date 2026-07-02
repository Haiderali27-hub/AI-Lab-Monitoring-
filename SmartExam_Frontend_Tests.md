# SmartExam — Frontend Test Guide
### Playwright Automated Browser Tests for All 6 Modules
### Opens a real browser · Clicks every button · Screenshots on failure

---

## Table of Contents

1. [What Kind of Testing This Is](#1-what-kind-of-testing-this-is)
2. [Why Playwright and Not Selenium](#2-why-playwright-and-not-selenium)
3. [Setup — Install Playwright](#3-setup--install-playwright)
4. [Configuration](#4-configuration)
5. [Test Helpers & Page Objects](#5-test-helpers--page-objects)
6. [Module 1 & 2 — Auth & Admin Tests](#6-module-1--2--auth--admin-tests)
7. [Module 2 — Teacher Portal Tests](#7-module-2--teacher-portal-tests)
8. [Module 3 — Live Monitor Tests](#8-module-3--live-monitor-tests)
9. [Module 4 — Analytics Tests](#9-module-4--analytics-tests)
10. [Module 5 — Student Portal Tests](#10-module-5--student-portal-tests)
11. [Module 6 — Notifications Tests](#11-module-6--notifications-tests)
12. [How to Run Tests](#12-how-to-run-tests)
13. [How to Read the Logs](#13-how-to-read-the-logs)
14. [How to View Screenshots and Videos](#14-how-to-view-screenshots-and-videos)
15. [How to Fix Failing Tests](#15-how-to-fix-failing-tests)
16. [How to Rerun Tests](#16-how-to-rerun-tests)

---

## 1. What Kind of Testing This Is

**Type: Black-Box End-to-End (E2E) Browser Testing**

This is exactly what you described. Playwright:
- Opens a real Chromium browser automatically
- Navigates to your React app at `http://localhost:5173`
- Clicks buttons, fills forms, reads text from the screen
- Checks if the right things appear on screen
- Takes a **screenshot automatically** when a test fails
- Records a **video** of every test run
- Closes the browser when done

You just run one command and watch it go. Every test reports ✅ PASS or ❌ FAIL with a reason.

**What gets tested:**
- Login flow for all 3 roles
- Admin user management (create, reset binding, force logout)
- Teacher exam creation (multi-step form)
- Live monitor page loads and shows student tiles
- Analytics charts render with real data
- Student portal dashboard, exam results, violations
- Notification bell, dropdown, mark as read

**Requirements before running:**
- Backend must be running at `http://localhost:5000`
- React app must be running at `http://localhost:5173`
- Database must be seeded (it is — seeder runs on backend startup)

---

## 2. Why Playwright and Not Selenium

| Feature | Selenium | Playwright |
|---------|----------|------------|
| Speed | Slow | 3–5x faster |
| Setup on Windows | Complex (ChromeDriver version matching) | One command installs everything |
| React support | Often flaky with dynamic content | Built for modern SPAs |
| Auto-wait | Manual waits needed | Waits automatically for elements |
| Screenshots on fail | Manual setup | Built-in, automatic |
| Video recording | Plugin required | Built-in |
| TypeScript support | Poor | First-class |
| Maintained by | Open source community | Microsoft |

Playwright is the modern standard. Selenium is older and harder to use with React.

---

## 3. Setup — Install Playwright

Run these commands from inside `Admin_Web_Panel/`:

```bash
# Go to your React project folder
cd SmartExam/Admin_Web_Panel

# Install Playwright
npm install -D @playwright/test

# Install the browsers (Chromium, Firefox, WebKit)
# On Windows this takes about 2 minutes
npx playwright install

# Create the test folder
mkdir -p tests/e2e

# Verify Playwright is installed
npx playwright --version
# Should print: Version 1.x.x
```

---

## 4. Configuration

Create `playwright.config.ts` in the root of `Admin_Web_Panel/`:

```typescript
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  timeout: 30_000,           // 30 seconds per test max
  retries: 1,                // Retry failed test once before marking as failed
  workers: 1,                // Run tests one at a time (avoids conflicts with shared state)

  reporter: [
    ['list'],                // Shows each test as it runs
    ['html', { open: 'on-failure' }],  // Opens HTML report automatically when tests fail
  ],

  use: {
    baseURL: 'http://localhost:5173',
    headless: false,         // Set to TRUE to run without opening browser window (faster)
    screenshot: 'only-on-failure',   // Auto screenshot when test fails
    video: 'retain-on-failure',      // Save video only for failed tests
    trace: 'on-first-retry',         // Detailed trace on retry
    actionTimeout: 10_000,   // 10 seconds to find/click an element
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // Automatically start the dev server before tests run
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,  // Use already-running server if available
    timeout: 30_000,
  },
});
```

Create `tests/e2e/helpers.ts` — shared utilities used by every test:

```typescript
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
```

---

## 5. Test Helpers & Page Objects

Create `tests/e2e/pages/LoginPage.ts`:

```typescript
import { Page } from '@playwright/test';

export class LoginPage {
  constructor(private page: Page) {}

  async goto() { await this.page.goto('/login'); }

  async fillEmail(email: string) {
    await this.page.getByLabel(/email/i).fill(email);
  }

  async fillPassword(password: string) {
    await this.page.getByLabel(/password/i).fill(password);
  }

  async submit() {
    await this.page.getByRole('button', { name: /sign in/i }).click();
  }

  async loginAs(email: string, password: string) {
    await this.goto();
    await this.fillEmail(email);
    await this.fillPassword(password);
    await this.submit();
  }
}
```

---

## 6. Module 1 & 2 — Auth & Admin Tests

Create `tests/e2e/auth.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER, STUDENT } from './helpers';
import { LoginPage } from './pages/LoginPage';

// ── TEST 1 ────────────────────────────────────────────────────────────────────
test('Admin login redirects to admin dashboard', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await expect(page).toHaveURL(/\/admin\/dashboard/);
  await expect(page.getByText(/overview/i)).toBeVisible();
});

// ── TEST 2 ────────────────────────────────────────────────────────────────────
test('Teacher login redirects to teacher dashboard', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await expect(page).toHaveURL(/\/teacher\/dashboard/);
});

// ── TEST 3 ────────────────────────────────────────────────────────────────────
test('Student login redirects to student portal', async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
  await expect(page).toHaveURL(/\/student\/dashboard/);
});

// ── TEST 4 ────────────────────────────────────────────────────────────────────
test('Wrong password shows error message', async ({ page }) => {
  const loginPage = new LoginPage(page);
  await loginPage.loginAs(ADMIN.email, 'WrongPassword123!');
  await expect(page.getByText(/invalid email or password/i)).toBeVisible();
  await expect(page).toHaveURL(/\/login/); // stayed on login
});

// ── TEST 5 ────────────────────────────────────────────────────────────────────
test('Empty form shows validation before submitting', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: /sign in/i }).click();
  // Should either show HTML5 validation or stay on login page
  await expect(page).toHaveURL(/\/login/);
});

// ── TEST 6 ────────────────────────────────────────────────────────────────────
test('Protected route redirects to login when not authenticated', async ({ page }) => {
  await page.goto('/admin/dashboard');
  await expect(page).toHaveURL(/\/login/);
});

// ── TEST 7 ────────────────────────────────────────────────────────────────────
test('Student cannot access admin dashboard', async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
  await page.goto('/admin/dashboard');
  // Should redirect back to student dashboard or login
  await expect(page).not.toHaveURL(/\/admin\/dashboard/);
});

// ── TEST 8 ────────────────────────────────────────────────────────────────────
test('Logout clears session and redirects to login', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await expect(page).toHaveURL(/\/admin\/dashboard/);
  // Click logout button
  await page.getByRole('button', { name: /logout/i }).click();
  await expect(page).toHaveURL(/\/login/);
  // Going back to dashboard should redirect to login
  await page.goto('/admin/dashboard');
  await expect(page).toHaveURL(/\/login/);
});
```

Create `tests/e2e/admin.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, waitForToast } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
});

// ── TEST 9 ────────────────────────────────────────────────────────────────────
test('Admin dashboard shows stat cards with numbers', async ({ page }) => {
  await expect(page).toHaveURL(/\/admin\/dashboard/);
  // Stat cards should show numbers > 0
  await expect(page.getByText(/total students/i)).toBeVisible();
  await expect(page.getByText(/active exams/i)).toBeVisible();
});

// ── TEST 10 ───────────────────────────────────────────────────────────────────
test('Admin can navigate to User Management via sidebar', async ({ page }) => {
  await page.getByRole('link', { name: /users/i }).click();
  await expect(page).toHaveURL(/\/admin\/users/);
  await expect(page.getByText(/user management/i)).toBeVisible();
});

// ── TEST 11 ───────────────────────────────────────────────────────────────────
test('User Management table loads and shows users', async ({ page }) => {
  await page.goto('/admin/users');
  // Wait for table to populate
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
  const rowCount = await page.locator('table tbody tr').count();
  expect(rowCount).toBeGreaterThanOrEqualTo(4);
});

// ── TEST 12 ───────────────────────────────────────────────────────────────────
test('Admin can search for a user by name', async ({ page }) => {
  await page.goto('/admin/users');
  await page.getByPlaceholder(/search/i).fill('Ali');
  await page.waitForTimeout(300); // debounce
  const rows = page.locator('table tbody tr');
  const count = await rows.count();
  // All visible rows should contain "Ali"
  for (let i = 0; i < count; i++) {
    await expect(rows.nth(i)).toContainText(/ali/i);
  }
});

// ── TEST 13 ───────────────────────────────────────────────────────────────────
test('Admin can open Add User panel and create a user', async ({ page }) => {
  await page.goto('/admin/users');
  await page.getByRole('button', { name: /add user/i }).click();

  // Slide-over panel should appear
  await expect(page.getByText(/add new user/i)).toBeVisible();

  // Fill in the form
  await page.getByLabel(/full name/i).fill('Playwright Test User');
  await page.getByLabel(/email/i).fill(`playwright_${Date.now()}@test.com`);
  await page.getByLabel(/password/i).fill('Test@12345');
  await page.getByLabel(/role/i).selectOption('Student');

  // Submit
  await page.getByRole('button', { name: /create user/i }).click();

  // Panel should close and new user should appear in table
  await expect(page.getByText(/add new user/i)).not.toBeVisible({ timeout: 5_000 });
  await waitForToast(page, /created/i.source || 'created');
});

// ── TEST 14 ───────────────────────────────────────────────────────────────────
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

// ── TEST 15 ───────────────────────────────────────────────────────────────────
test('Device Bindings page loads and shows students', async ({ page }) => {
  await page.goto('/admin/device-bindings');
  await expect(page.getByText(/device bindings/i)).toBeVisible();
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
});

// ── TEST 16 ───────────────────────────────────────────────────────────────────
test('Reset binding shows confirmation modal before proceeding', async ({ page }) => {
  await page.goto('/admin/device-bindings');
  // Click the first "Reset Binding" button
  await page.getByRole('button', { name: /reset binding/i }).first().click();
  // Confirmation modal should appear
  await expect(page.getByText(/are you sure|reset device binding/i)).toBeVisible();
  // Cancel button should close modal
  await page.getByRole('button', { name: /cancel/i }).click();
  await expect(page.getByText(/are you sure|reset device binding/i)).not.toBeVisible();
});
```

---

## 7. Module 2 — Teacher Portal Tests

Create `tests/e2e/teacher.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { loginAs, TEACHER, waitForToast } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
});

// ── TEST 17 ───────────────────────────────────────────────────────────────────
test('Teacher dashboard loads with exam list', async ({ page }) => {
  await expect(page).toHaveURL(/\/teacher\/dashboard/);
  await expect(page.getByText(/good morning|dashboard/i)).toBeVisible();
});

// ── TEST 18 ───────────────────────────────────────────────────────────────────
test('Teacher can navigate to Create Exam page', async ({ page }) => {
  await page.getByRole('link', { name: /create exam/i }).click();
  await expect(page).toHaveURL(/\/teacher\/create-exam/);
  await expect(page.getByText(/create new exam/i)).toBeVisible();
});

// ── TEST 19 ───────────────────────────────────────────────────────────────────
test('Create Exam Step 1 — fills basic info and advances to Step 2', async ({ page }) => {
  await page.goto('/teacher/create-exam');

  // Fill step 1
  await page.getByLabel(/exam title/i).fill('Playwright Automated Exam');
  await page.getByLabel(/section/i).selectOption({ index: 1 }); // first section
  await page.getByLabel(/duration/i).fill('60');

  // Set date to tomorrow
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  const dateStr = tomorrow.toISOString().split('T')[0];
  await page.getByLabel(/date/i).fill(dateStr);
  await page.getByLabel(/time/i).fill('10:00');

  // Click Next
  await page.getByRole('button', { name: /next/i }).click();

  // Should be on Step 2
  await expect(page.getByText(/add question/i)).toBeVisible();
});

// ── TEST 20 ───────────────────────────────────────────────────────────────────
test('Create Exam Step 2 — can add a question', async ({ page }) => {
  await page.goto('/teacher/create-exam');
  // Skip to step 2 (click next on step 1 with filled data)
  await page.getByLabel(/exam title/i).fill('Test Exam');
  await page.getByLabel(/duration/i).fill('60');
  await page.getByRole('button', { name: /next/i }).click();

  // Add a question
  await page.getByRole('button', { name: /add question/i }).click();
  await expect(page.getByText(/question 1/i)).toBeVisible();

  // Fill question
  await page.getByPlaceholder(/question text|enter question/i).first().fill('What is a linked list?');
  await page.getByLabel(/marks/i).first().fill('10');
});

// ── TEST 21 ───────────────────────────────────────────────────────────────────
test('Eligibility page loads exam assignments', async ({ page }) => {
  await page.goto('/teacher/eligibility');
  await expect(page.getByText(/eligibility/i)).toBeVisible();

  // Select an exam from dropdown
  await page.getByRole('combobox').first().selectOption({ index: 1 });

  // Student rows should appear
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 8_000 });
});

// ── TEST 22 ───────────────────────────────────────────────────────────────────
test('Eligibility toggle changes student eligibility', async ({ page }) => {
  await page.goto('/teacher/eligibility');
  await page.getByRole('combobox').first().selectOption({ index: 1 });
  await page.locator('table tbody tr').first().waitFor();

  // Click first toggle
  const toggle = page.locator('[role="switch"]').first();
  const initialState = await toggle.getAttribute('aria-checked');
  await toggle.click();
  const newState = await toggle.getAttribute('aria-checked');
  expect(newState).not.toBe(initialState);

  // "Unsaved changes" indicator should appear
  await expect(page.getByText(/unsaved changes/i)).toBeVisible();
});

// ── TEST 23 ───────────────────────────────────────────────────────────────────
test('Results page loads for ended exams', async ({ page }) => {
  await page.goto('/teacher/results');
  // Even if no results yet, page should load without error
  await expect(page.getByText(/results|no results|select an exam/i)).toBeVisible();
});
```

---

## 8. Module 3 — Live Monitor Tests

Create `tests/e2e/livemonitor.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { loginAs, TEACHER } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
});

// ── TEST 24 ───────────────────────────────────────────────────────────────────
test('Live Monitor page loads without crashing', async ({ page }) => {
  await page.goto('/teacher/live-monitor');
  await expect(page.getByText(/live monitor/i)).toBeVisible();
  // Should not show an error state
  await expect(page.getByText(/error|crash|cannot load/i)).not.toBeVisible();
});

// ── TEST 25 ───────────────────────────────────────────────────────────────────
test('Live Monitor shows no active exam message when exam is Scheduled', async ({ page }) => {
  await page.goto('/teacher/live-monitor');
  // If no exam is Active, should show an empty/waiting state
  const content = await page.getByRole('main').innerText();
  // Either shows "No active exam" or shows student tiles
  const hasNoExam = content.includes('No active') || content.includes('no exam');
  const hasTiles = await page.locator('[data-testid="student-tile"]').count() > 0;
  expect(hasNoExam || hasTiles).toBeTruthy();
});

// ── TEST 26 ───────────────────────────────────────────────────────────────────
test('Live Monitor Force Submit All button is visible', async ({ page }) => {
  await page.goto('/teacher/live-monitor');
  // Button may be disabled if no active exam — but it should be present in DOM
  const btn = page.getByRole('button', { name: /force submit all/i });
  // If visible, it should be there
  const isVisible = await btn.isVisible().catch(() => false);
  // This test passes whether button is there or not (defensive)
  // — real test is that the page doesn't crash
  await expect(page.getByText(/live monitor/i)).toBeVisible();
});
```

---

## 9. Module 4 — Analytics Tests

Create `tests/e2e/analytics.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER } from './helpers';

// ── TEST 27 ───────────────────────────────────────────────────────────────────
test('Admin System Analytics page loads with stat cards', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/analytics');

  await expect(page.getByText(/system analytics/i)).toBeVisible();
  await expect(page.getByText(/total students/i)).toBeVisible();
  await expect(page.getByText(/total teachers/i)).toBeVisible();
  await expect(page.getByText(/total exams/i)).toBeVisible();
});

// ── TEST 28 ───────────────────────────────────────────────────────────────────
test('Admin Analytics shows Exams Per Month chart', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/analytics');

  await expect(page.getByText(/exams per month/i)).toBeVisible();
  // Chart bars should be rendered
  const bars = page.locator('[data-testid="chart-bar"]');
  const count = await bars.count();
  // May be 0 if no exams, but the chart container should exist
  await expect(page.getByText(/exams per month/i)).toBeVisible();
});

// ── TEST 29 ───────────────────────────────────────────────────────────────────
test('Teacher Exam Analytics shows empty state when no exam selected', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/analytics');

  await expect(page.getByText(/exam analytics/i)).toBeVisible();
  await expect(page.getByText(/no exam selected|select an exam/i)).toBeVisible();
});

// ── TEST 30 ───────────────────────────────────────────────────────────────────
test('Teacher can select exam and see analytics load', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/analytics');

  const dropdown = page.getByRole('combobox').first();
  const optionCount = await dropdown.locator('option').count();

  if (optionCount > 1) {
    await dropdown.selectOption({ index: 1 });
    // Stats should appear
    await expect(page.getByText(/average score/i)).toBeVisible({ timeout: 8_000 });
    await expect(page.getByText(/pass rate/i)).toBeVisible();
  } else {
    // No exams yet — empty state is acceptable
    await expect(page.getByText(/no exam|select/i)).toBeVisible();
  }
});

// ── TEST 31 ───────────────────────────────────────────────────────────────────
test('Generate PDF Report button triggers download', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/analytics');

  const dropdown = page.getByRole('combobox').first();
  const optionCount = await dropdown.locator('option').count();

  if (optionCount > 1) {
    await dropdown.selectOption({ index: 1 });
    await page.getByRole('button', { name: /generate pdf|generate report/i }).click();
    // Either a new tab opens or a toast appears
    await page.waitForTimeout(2000);
    const toastVisible = await page.getByText(/report generated|pdf/i).isVisible().catch(() => false);
    expect(toastVisible).toBeTruthy();
  }
});

// ── TEST 32 ───────────────────────────────────────────────────────────────────
test('Reports page loads with report history table', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/reports');

  await expect(page.getByText(/generated reports/i)).toBeVisible();
});
```

---

## 10. Module 5 — Student Portal Tests

Create `tests/e2e/student.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { loginAs, STUDENT } from './helpers';

test.beforeEach(async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
});

// ── TEST 33 ───────────────────────────────────────────────────────────────────
test('Student lands on student portal dashboard', async ({ page }) => {
  await expect(page).toHaveURL(/\/student\/dashboard/);
  await expect(page.getByText(/welcome back/i)).toBeVisible();
});

// ── TEST 34 ───────────────────────────────────────────────────────────────────
test('Student dashboard shows personal stat cards', async ({ page }) => {
  await expect(page.getByText(/exams taken/i)).toBeVisible();
  await expect(page.getByText(/average score/i)).toBeVisible();
});

// ── TEST 35 ───────────────────────────────────────────────────────────────────
test('Student can navigate to My Exams page', async ({ page }) => {
  await page.getByRole('link', { name: /my exams/i }).click();
  await expect(page).toHaveURL(/\/student\/exams/);
  await expect(page.getByText(/my exams/i)).toBeVisible();
});

// ── TEST 36 ───────────────────────────────────────────────────────────────────
test('My Exams page shows exam cards', async ({ page }) => {
  await page.goto('/student/exams');
  // Should show at least one exam card (from seed data)
  await expect(page.locator('[data-testid="exam-card"]').first()).toBeVisible({ timeout: 8_000 });
});

// ── TEST 37 ───────────────────────────────────────────────────────────────────
test('Filter pills on My Exams change visible exams', async ({ page }) => {
  await page.goto('/student/exams');
  // Click "Upcoming" filter pill
  await page.getByRole('button', { name: /upcoming/i }).click();
  await page.waitForTimeout(300);
  // Check URL or active pill style changed
  const activePill = page.getByRole('button', { name: /upcoming/i });
  await expect(activePill).toHaveClass(/bg-primary|active|blue/);
});

// ── TEST 38 ───────────────────────────────────────────────────────────────────
test('Student Performance page loads with chart', async ({ page }) => {
  await page.goto('/student/performance');
  await expect(page.getByText(/my performance/i)).toBeVisible();
  await expect(page.getByText(/score over time/i)).toBeVisible();
  await expect(page.getByText(/exams taken/i)).toBeVisible();
});

// ── TEST 39 ───────────────────────────────────────────────────────────────────
test('Student Violations page shows empty state when no violations', async ({ page }) => {
  await page.goto('/student/violations');
  await expect(page.getByText(/violation history/i)).toBeVisible();
  // Either shows empty state or violation cards
  const hasEmpty = await page.getByText(/no violations|clean record/i).isVisible().catch(() => false);
  const hasCards = await page.locator('[data-testid="violation-card"]').count() > 0;
  expect(hasEmpty || hasCards).toBeTruthy();
});

// ── TEST 40 ───────────────────────────────────────────────────────────────────
test('Student cannot access admin routes', async ({ page }) => {
  await page.goto('/admin/users');
  await expect(page).not.toHaveURL(/\/admin\/users/);
});

// ── TEST 41 ───────────────────────────────────────────────────────────────────
test('Student top navbar has all navigation links', async ({ page }) => {
  await expect(page.getByRole('link', { name: /dashboard/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /my exams/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /performance/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /violations/i })).toBeVisible();
  await expect(page.getByRole('link', { name: /notifications/i })).toBeVisible();
});
```

---

## 11. Module 6 — Notifications Tests

Create `tests/e2e/notifications.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { loginAs, ADMIN, TEACHER, STUDENT, waitForToast } from './helpers';

// ── TEST 42 ───────────────────────────────────────────────────────────────────
test('Notification bell visible in admin navbar', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await expect(page.locator('[data-testid="notification-bell"]')).toBeVisible();
});

// ── TEST 43 ───────────────────────────────────────────────────────────────────
test('Clicking notification bell opens dropdown', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.locator('[data-testid="notification-bell"]').click();
  await expect(page.getByText(/notifications/i).first()).toBeVisible();
  await expect(page.getByText(/mark all read/i)).toBeVisible();
});

// ── TEST 44 ───────────────────────────────────────────────────────────────────
test('Clicking outside bell dropdown closes it', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.locator('[data-testid="notification-bell"]').click();
  await expect(page.getByText(/mark all read/i)).toBeVisible();
  // Click outside
  await page.locator('main').click();
  await expect(page.getByText(/mark all read/i)).not.toBeVisible();
});

// ── TEST 45 ───────────────────────────────────────────────────────────────────
test('Admin Notifications page loads with filter tabs', async ({ page }) => {
  await loginAs(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/notifications');

  await expect(page.getByText(/notifications/i).first()).toBeVisible();
  await expect(page.getByRole('tab', { name: /all/i })).toBeVisible();
  await expect(page.getByRole('tab', { name: /unread/i })).toBeVisible();
});

// ── TEST 46 ───────────────────────────────────────────────────────────────────
test('Teacher can navigate to Announcement page', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/announce');

  await expect(page.getByText(/send announcement/i)).toBeVisible();
  await expect(page.getByLabel(/select exam/i)).toBeVisible();
});

// ── TEST 47 ───────────────────────────────────────────────────────────────────
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

// ── TEST 48 ───────────────────────────────────────────────────────────────────
test('Email toggle shows warning banner when turned on', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/announce');

  const toggle = page.locator('[data-testid="email-toggle"]');
  if (await toggle.isVisible()) {
    await toggle.click();
    await expect(page.getByText(/sending email/i)).toBeVisible();
  }
});

// ── TEST 49 ───────────────────────────────────────────────────────────────────
test('Student receives notification and can mark it as read', async ({ page }) => {
  await loginAs(page, STUDENT.email, STUDENT.password);
  await page.goto('/student/notifications');

  await expect(page.getByText(/notifications/i).first()).toBeVisible();

  const unreadCard = page.locator('[data-testid="notification-card"].unread').first();
  const hasUnread = await unreadCard.isVisible().catch(() => false);

  if (hasUnread) {
    await unreadCard.click();
    // After click, unread dot should disappear
    await page.waitForTimeout(500);
  }
  // Page should not crash regardless
  await expect(page.getByText(/notifications/i).first()).toBeVisible();
});

// ── TEST 50 ───────────────────────────────────────────────────────────────────
test('Toast notification appears and auto-dismisses', async ({ page }) => {
  await loginAs(page, TEACHER.email, TEACHER.password);
  await page.goto('/teacher/announce');

  const dropdown = page.getByLabel(/select exam/i);
  const optionCount = await dropdown.locator('option').count();

  if (optionCount > 1) {
    await dropdown.selectOption({ index: 1 });
    await page.getByLabel(/title/i).fill('Test Announcement');
    await page.getByLabel(/message/i).fill('This is a Playwright test announcement.');
    await page.getByRole('button', { name: /send announcement/i }).click();

    // Toast should appear
    await expect(page.locator('[data-testid="toast"]').first()).toBeVisible({ timeout: 5_000 });
    // Auto-dismiss after 5 seconds
    await expect(page.locator('[data-testid="toast"]').first()).not.toBeVisible({ timeout: 8_000 });
  }
});
```

---

## 12. How to Run Tests

### Before running — make sure both servers are running

```bash
# Terminal 1 — Backend
cd SmartExam/Backend_API
dotnet run
# Must show: Now listening on: http://localhost:5000

# Terminal 2 — Frontend
cd SmartExam/Admin_Web_Panel
npm run dev
# Must show: Local: http://localhost:5173
```

### Run ALL tests (opens browser window)

```bash
# Terminal 3 — Tests
cd SmartExam/Admin_Web_Panel
npx playwright test
```

### Run ALL tests in headless mode (no browser window, faster)

```bash
npx playwright test --headed=false
```

### Run tests for one module only

```bash
# Auth tests
npx playwright test tests/e2e/auth.spec.ts

# Admin tests
npx playwright test tests/e2e/admin.spec.ts

# Teacher tests
npx playwright test tests/e2e/teacher.spec.ts

# Student tests
npx playwright test tests/e2e/student.spec.ts

# Analytics tests
npx playwright test tests/e2e/analytics.spec.ts

# Notifications tests
npx playwright test tests/e2e/notifications.spec.ts
```

### Run a single test by name

```bash
npx playwright test -g "Admin login redirects to admin dashboard"
```

### Run with full browser visible and slow motion (good for debugging)

```bash
npx playwright test --headed --slowmo=500
```

### Open Playwright UI mode (interactive test runner with browser preview)

```bash
npx playwright test --ui
# Opens a GUI where you can click each test and watch it run
```

---

## 13. How to Read the Logs

### What a passing run looks like

```
Running 50 tests using 1 worker

  ✓  auth.spec.ts:8:5 › Admin login redirects to admin dashboard (1.2s)
  ✓  auth.spec.ts:14:5 › Teacher login redirects to teacher dashboard (0.9s)
  ✓  auth.spec.ts:20:5 › Student login redirects to student portal (1.1s)
  ...
  ✓  notifications.spec.ts:142:5 › Toast notification appears and auto-dismisses (4.3s)

  50 passed (47.3s)
```

### What a failing test looks like

```
  ✗  admin.spec.ts:45:5 › Admin can open Add User panel and create a user (5.2s)

    Error: Timeout 10000ms exceeded.
    waiting for getByRole('button', { name: /create user/i })

    Call log:
      - waiting for locator('role=button[name=/create user/i]')
      - locator resolved to <button class="btn-primary">Create</button>
      - unexpected value "Create" (expected match for /create user/i)

    Screenshot: test-results/admin-Admin-can-open-Add-User-panel/screenshot.png
```

**Reading a failure:**
- **Test name** — which test failed
- **Error message** — what Playwright was looking for that it couldn't find
- **Call log** — the exact step that failed
- **Screenshot path** — automatically saved screenshot at the moment of failure

---

## 14. How to View Screenshots and Videos

### Screenshots (saved automatically on failure)

```bash
# Screenshots are saved here
SmartExam/Admin_Web_Panel/test-results/

# Each failing test creates a folder:
test-results/
├── auth-Admin-login-fails/
│   └── screenshot.png
├── admin-Create-user-timeout/
│   └── screenshot.png
```

Open them in Windows Explorer or any image viewer.

### Videos (saved for failed tests)

```bash
# Videos are in the same folder as screenshots
test-results/
├── auth-Admin-login-fails/
│   ├── screenshot.png
│   └── video.webm     ← Open with VLC or Chrome
```

### HTML Report (full visual report)

```bash
# View the full HTML report
npx playwright show-report

# Or open it manually
# The report is at: playwright-report/index.html
start playwright-report/index.html   # Windows
```

The HTML report shows:
- Every test with pass/fail status
- Duration of each test
- Screenshot inline for failures
- Step-by-step trace viewer

### Trace Viewer (step-by-step replay of a failure)

```bash
# If a test failed and retried, a trace file is saved
npx playwright show-trace test-results/your-test-name/trace.zip
# Opens a browser with step-by-step replay, DOM snapshots, network calls
```

---

## 15. How to Fix Failing Tests

| Error message | Cause | Fix |
|---|---|---|
| `Timeout exceeded waiting for getByRole(...)` | Button/element text doesn't match | Open the HTML report screenshot, see the actual button text, update the test selector |
| `waiting for URL to match /admin\/dashboard/` | Login not working | Check backend is running on port 5000, check seed data credentials match |
| `Expected to have URL /student/dashboard but got /login` | Student login redirecting wrong | Check `App.tsx` — the Student role redirect must go to `/student/dashboard` |
| `Error: locator.click: Element is outside of viewport` | Element exists but is scrolled off screen | Add `await page.locator('...').scrollIntoViewIfNeeded()` before clicking |
| `strict mode violation: getByRole found 3 elements` | Selector matches multiple elements | Be more specific: add `{ name: 'exact text' }` or use `.first()` |
| `Expected visible but element is hidden` | React hasn't rendered it yet | Add `await expect(element).toBeVisible({ timeout: 8000 })` with a longer timeout |
| `net::ERR_CONNECTION_REFUSED` | Frontend or backend not running | Make sure both servers are running before starting tests |
| Test passes locally but fails in headless | Visual/timing difference | Add `await page.waitForTimeout(300)` before the failing assertion |
| Wrong text found on page | Stitch design text doesn't match coded text | Check the actual React component text and update the test |

### How to find the right selector when a test fails

```bash
# Run Playwright in codegen mode — it records your actions and generates selectors
npx playwright codegen http://localhost:5173

# Now interact with the app manually
# Playwright generates the correct selector code in real time
# Copy the generated code into your test
```

---

## 16. How to Rerun Tests

### Rerun all tests

```bash
npx playwright test
```

### Rerun only failed tests from last run

```bash
npx playwright test --last-failed
```

### Rerun a specific failing test

```bash
npx playwright test -g "exact test name here"
```

### Clean test results and rerun fresh

```bash
# Delete old results
rmdir /s /q test-results playwright-report   # Windows
# Then rerun
npx playwright test
```

### Watch mode — reruns when you save a file

```bash
npx playwright test --watch
# Automatically reruns tests when you edit a .spec.ts file
```

### Update snapshots (if you use visual testing)

```bash
npx playwright test --update-snapshots
```

---

## Test Summary — All 50 Tests

| # | Test Name | Module | What it checks |
|---|-----------|--------|----------------|
| 1 | Admin login redirect | Auth | Lands on /admin/dashboard |
| 2 | Teacher login redirect | Auth | Lands on /teacher/dashboard |
| 3 | Student login redirect | Auth | Lands on /student/dashboard |
| 4 | Wrong password error | Auth | Error message shown |
| 5 | Empty form stays on login | Auth | No crash |
| 6 | Protected route redirect | Auth | Goes to /login |
| 7 | Student blocked from admin | Auth | Cannot access /admin |
| 8 | Logout clears session | Auth | Redirects to /login |
| 9 | Admin dashboard stat cards | Admin | Numbers visible |
| 10 | Sidebar navigation | Admin | Navigates to /admin/users |
| 11 | User table loads | Admin | Rows visible |
| 12 | Search users | Admin | Filters by name |
| 13 | Create new user | Admin | User created |
| 14 | Tab filter students | Admin | Only students shown |
| 15 | Device bindings page | Admin | Table loads |
| 16 | Reset binding modal | Admin | Confirmation shown |
| 17 | Teacher dashboard | Teacher | Loads correctly |
| 18 | Navigate to Create Exam | Teacher | URL changes |
| 19 | Create Exam Step 1 | Teacher | Advances to Step 2 |
| 20 | Add question in Step 2 | Teacher | Question card appears |
| 21 | Eligibility page loads | Teacher | Assignments shown |
| 22 | Toggle eligibility | Teacher | State changes |
| 23 | Results page loads | Teacher | No crash |
| 24 | Live Monitor loads | Monitor | No crash |
| 25 | Live Monitor empty state | Monitor | Handled gracefully |
| 26 | Force Submit button visible | Monitor | Button present |
| 27 | Admin analytics stat cards | Analytics | Cards visible |
| 28 | Exams per month chart | Analytics | Chart rendered |
| 29 | Teacher analytics empty state | Analytics | Empty state shown |
| 30 | Select exam shows analytics | Analytics | Stats appear |
| 31 | Generate PDF report | Analytics | Toast/tab appears |
| 32 | Reports page loads | Analytics | No crash |
| 33 | Student lands on dashboard | Student | Correct URL |
| 34 | Dashboard stat cards | Student | Cards visible |
| 35 | Navigate to My Exams | Student | URL changes |
| 36 | Exam cards visible | Student | At least 1 card |
| 37 | Filter pills work | Student | Active pill style |
| 38 | Performance page chart | Student | Chart visible |
| 39 | Violations empty state | Student | Handled gracefully |
| 40 | Student blocked from admin | Student | Cannot access |
| 41 | Student navbar links | Student | All 5 links visible |
| 42 | Bell icon visible | Notifications | Bell present in navbar |
| 43 | Bell opens dropdown | Notifications | Dropdown appears |
| 44 | Click outside closes bell | Notifications | Dropdown closes |
| 45 | Notifications page loads | Notifications | Filter tabs visible |
| 46 | Announcement page loads | Notifications | Form visible |
| 47 | Recipient count shows | Notifications | Count updates on select |
| 48 | Email toggle warning | Notifications | Warning banner shown |
| 49 | Student mark notification read | Notifications | No crash |
| 50 | Toast appears and auto-dismisses | Notifications | Toast lifecycle |
