import { test, expect } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import { examWithStudents, signInWeb } from '../support/scenario';

// Module 7 — the section's teacher assigns invigilators to an exam from the Eligibility page;
// the assignment is what puts the exam into the invigilator's live monitor.

test.describe('M7 — Invigilator assignment (teacher UI)', () => {
  test('M7-E-07 Teacher assigns and removes an invigilator', async ({ page, request }) => {
    const { admin, teacher, exam } = await examWithStudents(request, 1);
    const invigilator = await admin.createInvigilator('Proctor');

    await signInWeb(page, teacher);
    await page.goto(`/teacher/eligibility?examId=${exam.examId}`);
    const panel = page.getByTestId('proctors-panel');
    await expect(panel).toContainText('No invigilator assigned.');

    await panel.getByTestId('proctor-select').selectOption({ label: `${invigilator.name} (${invigilator.email})` });
    await panel.getByRole('button', { name: 'Assign Invigilator' }).click();
    await expect(panel.getByTestId('proctor-chip')).toHaveText([new RegExp(invigilator.name)]);
    expect((await teacher.proctors(exam.examId)).map(p => p.userId)).toEqual([invigilator.userId]);

    // The invigilator now sees exactly this exam (server-scoped list).
    const asInvigilator = await StaffApi.login(request, invigilator.email, invigilator.password);
    expect((await asInvigilator.exams()).map(e => e.examId)).toEqual([exam.examId]);

    page.once('dialog', d => d.accept());
    await panel.getByRole('button', { name: `Remove ${invigilator.name}` }).click();
    await expect(panel.getByTestId('proctor-chip')).toHaveCount(0);
    expect(await asInvigilator.exams()).toEqual([]);
  });

  test('M7-E-08 Teacher of another section gets no live data for an exam they do not supervise', async ({ page, request }) => {
    const { admin, teacher, exam, students, users } = await examWithStudents(request, 1);
    await teacher.endAllActiveExams();
    await students[0].start(exam.examId);
    const otherTeacherUser = await admin.createUser('Teacher', 'Other');
    const otherTeacher = await StaffApi.login(request, otherTeacherUser.email, otherTeacherUser.password);

    await signInWeb(page, otherTeacher);
    await page.goto(`/teacher/live-monitor?examId=${exam.examId}`);
    await expect(page.getByText(`Live Monitor — ${exam.title}`)).toBeVisible();
    await expect(page.getByTestId('join-denied')).toContainText(/not assigned to supervise/i);
    expect((await students[0].heartbeat()).status()).toBe(200);
    await expect(page.getByTestId('student-tile').filter({ hasText: users[0].name })).toHaveCount(0);

    // Once the exam's teacher assigns them as a co-proctor, the same page works.
    expect((await teacher.assignProctor(exam.examId, otherTeacherUser.userId)).status()).toBe(201);
    await page.reload();
    await expect(page.getByTestId('join-denied')).toHaveCount(0);
    await expect(async () => {
      await students[0].heartbeat();
      await expect(page.getByTestId('student-tile').filter({ hasText: users[0].name })).toBeVisible({ timeout: 1_000 });
    }).toPass({ timeout: 20_000 });
  });

  test('M7-E-09 Unassigning an invigilator stops their open live monitor', async ({ page, request }) => {
    const { admin, teacher, exam, students, users } = await examWithStudents(request, 1);
    await students[0].start(exam.examId);
    const invigilatorUser = await admin.createInvigilator('Removed');
    expect((await teacher.assignProctor(exam.examId, invigilatorUser.userId)).status()).toBe(201);
    const invigilator = await StaffApi.login(request, invigilatorUser.email, invigilatorUser.password);

    await signInWeb(page, invigilator);
    await page.goto(`/invigilator/live-monitor?examId=${exam.examId}`);
    const tile = page.getByTestId('student-tile').filter({ hasText: users[0].name });
    await expect(async () => {
      await students[0].heartbeat();
      await expect(tile).toBeVisible({ timeout: 1_000 });
    }).toPass({ timeout: 20_000 });

    // Removed while the page is open: the server drops the connection from the exam's group at once.
    expect((await teacher.unassignProctor(exam.examId, invigilatorUser.userId)).status()).toBe(204);
    await expect(page.getByTestId('join-denied')).toContainText(/access to this exam was withdrawn/i);
    await expect(tile).toHaveCount(0);
    expect((await students[0].heartbeat()).status()).toBe(200);
    await page.waitForTimeout(1_500); // a broadcast would have arrived by now
    await expect(tile).toHaveCount(0);
  });
});
