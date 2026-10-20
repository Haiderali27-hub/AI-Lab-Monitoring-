import { test, expect } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import { examWithStudents, signInStudentWeb } from '../support/scenario';

test.describe('M3 — Student Exam Dashboard (web portal)', () => {
  test('M3-E-01 Student portal lists exam', async ({ page, request }) => {
    const { users, exam } = await examWithStudents(request, 1);
    await signInStudentWeb(page, request, users[0]);
    await page.goto('/student/exams');

    await expect(page.getByRole('heading', { name: 'My Exams' })).toBeVisible();
    const card = page.locator('div.grid > div').filter({ hasText: exam.title });
    await expect(card).toBeVisible();
    await expect(card).toContainText('Scheduled');
  });

  test('M3-E-02 Student result page', async ({ page, request }) => {
    const { users, students, exam, teacher } = await examWithStudents(request, 1);
    const mcq = exam.questions.find(q => q.type === 'Mcq')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(mcq.questionId, '1');   // 5 marks
    expect((await students[0].submit()).status()).toBe(200);
    expect((await teacher.forceSubmitAll(exam.examId)).status()).toBe(200); // ends the exam → results released

    await signInStudentWeb(page, request, users[0]);
    await page.goto('/student/exams');
    const card = page.locator('div.grid > div').filter({ hasText: exam.title });
    await card.getByRole('button', { name: 'View Detailed Results' }).click();
    await expect(page).toHaveURL(new RegExp(`/student/exams/${exam.examId}/result`));
    await expect(page.getByRole('heading', { name: exam.title })).toBeVisible();
    await expect(page.getByText('5 / 35 Marks')).toBeVisible();
    await expect(page.getByText('14.3%')).toBeVisible();
  });

  test.fixme('M3-E-03 Countdown not available before start', async ({ page, request }) => {
    const { users } = await examWithStudents(request, 1, { startTime: new Date(Date.now() + 60 * 60_000) });
    await signInStudentWeb(page, request, users[0]);
    await page.goto('/student/dashboard');
    await expect(page.getByText(/starts in/i)).toBeVisible();
    await expect(page.getByText(/remaining/i)).toHaveCount(0);
  });
});

test.describe('M4 — Secure Exam Environment (student portal)', () => {
  test('M4-E-04 Student violations page', async ({ page, request }) => {
    const { users, students, exam } = await examWithStudents(request, 1);
    await students[0].start(exam.examId);
    expect((await students[0].violation('FocusLoss', 'Discord')).status()).toBe(200);
    expect((await students[0].violations()).totalViolations).toBe(1);

    await signInStudentWeb(page, request, users[0]);
    await page.goto('/student/violations');
    await expect(page.getByRole('heading', { name: 'Violation History' })).toBeVisible();
    await expect(page.getByText(exam.title)).toBeVisible();
    await expect(page.getByRole('heading', { name: /academic integrity secured/i })).toHaveCount(0);
  });

  test('M4-E-04 Student violations page — clean student sees empty state', async ({ page, request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    await signInStudentWeb(page, request, user);
    await page.goto('/student/violations');
    await expect(page.getByRole('heading', { name: /academic integrity secured/i })).toBeVisible();
  });
});
