import { test, expect } from '@playwright/test';
import { API_URL } from '../support/env';
import { examWithStudents, signInStudentWeb, signInWeb } from '../support/scenario';

// Module 2 — Pre-Exam Verification & Mock Test is not built yet. These are the executable
// acceptance criteria (planned: POST /api/student/readiness, Exam.RequireWebcam, mock exams).

test.describe('M2 — Pre-Exam Verification & Mock Test', () => {
  test.fixme('M2-E-01 Teacher sees readiness column', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    // Simulator posts an all-pass readiness result
    await request.post(`${API_URL}/api/student/readiness`, {
      headers: { Authorization: `Bearer ${students[0].token}` },
      data: { examId: exam.examId, checks: [{ name: 'Network', passed: true }, { name: 'Camera', passed: true }] },
    });
    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);
    const row = page.locator('tbody tr').filter({ hasText: users[0].name });
    await expect(row.getByTestId('readiness-status')).toHaveText('✓');
  });

  test.fixme('M2-E-02 Failed readiness visible', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    await request.post(`${API_URL}/api/student/readiness`, {
      headers: { Authorization: `Bearer ${students[0].token}` },
      data: { examId: exam.examId, checks: [{ name: 'Camera', passed: false, detail: 'No device' }] },
    });
    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);
    const status = page.locator('tbody tr').filter({ hasText: users[0].name }).getByTestId('readiness-status');
    await expect(status).toHaveText('✗');
    await status.hover();
    await expect(page.getByRole('tooltip')).toContainText('No device');
  });

  test.fixme('M2-E-03 Create exam: readiness toggles', async ({ page, request }) => {
    const { teacher } = await examWithStudents(request, 0);
    await signInWeb(page, teacher);
    await page.goto('/teacher/create-exam');
    // ...steps 1–2, then on "Rules & Settings":
    await page.getByLabel(/require webcam/i).check();
    // ...create, reopen edit page → still checked
    await expect(page.getByLabel(/require webcam/i)).toBeChecked();
  });

  test.fixme('M2-E-04 Practice exam labelled', async ({ page, request }) => {
    const { users } = await examWithStudents(request, 1, { title: `Mock ${Date.now()}` /* isMock: true */ });
    await signInStudentWeb(page, request, users[0]);
    await page.goto('/student/exams');
    await expect(page.getByText('Practice')).toBeVisible();
  });
});
