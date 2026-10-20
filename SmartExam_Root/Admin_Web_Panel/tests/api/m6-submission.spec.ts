import { test, expect } from '@playwright/test';
import { sleep } from '../support/env';
import { examWithStudents } from '../support/scenario';

// Must match SubmissionService.DeadlineGrace on the server.
const DEADLINE_GRACE_MS = 2 * 60_000;

test.describe('M6 — Submission & Backup', () => {
  test('M6-A-01 Save answer in window', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);

    const theory = exam.questions.find(q => q.type === 'Theory')!;
    const res = await student.saveAnswer(theory.questionId, 'A stack is LIFO.');
    expect(res.status()).toBe(200);
    expect((await res.json()).lastSavedAt).toBeTruthy();

    // Saving again updates (not duplicates) the answer
    expect((await student.saveAnswer(theory.questionId, 'A stack is LIFO, a queue is FIFO.')).status()).toBe(200);
    const [row] = await teacher.results(exam.examId);
    expect(row.answers).toHaveLength(1);
    expect(row.answers[0].answerText).toBe('A stack is LIFO, a queue is FIFO.');
  });

  test('M6-A-02 Save after deadline rejected', async ({ request }) => {
    test.setTimeout(60_000);
    // Window closes (end + grace) ~6 s from now: start-session still succeeds, then we wait it out.
    const durationMinutes = 1;
    const closesAt = Date.now() + 6_000;
    const startTime = new Date(closesAt - DEADLINE_GRACE_MS - durationMinutes * 60_000);
    const { students, exam } = await examWithStudents(request, 1, { startTime, durationMinutes });
    const student = students[0];
    await student.start(exam.examId);

    const q = exam.questions[1];
    expect((await student.saveAnswer(q.questionId, 'before deadline')).status()).toBe(200);

    await sleep(Math.max(0, closesAt - Date.now()) + 1_500);
    const late = await student.saveAnswer(q.questionId, 'after deadline');
    expect(late.status()).toBe(400);
    expect((await late.json()).message).toMatch(/time is over/i);
  });

  test('M6-A-03 Save after submit rejected', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);
    const q = exam.questions[0];
    expect((await student.saveAnswer(q.questionId, '1')).status()).toBe(200);
    expect((await student.submit()).status()).toBe(200);

    const res = await student.saveAnswer(q.questionId, '0');
    expect(res.status()).toBe(400);
  });

  test('M6-A-04 Double submit rejected', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);

    const first = await student.submit();
    expect(first.status()).toBe(200);
    expect((await first.json()).submittedAt).toBeTruthy();

    const second = await student.submit();
    expect(second.status()).toBe(400);
    expect((await second.json()).message).toMatch(/not active/i);
  });
});
