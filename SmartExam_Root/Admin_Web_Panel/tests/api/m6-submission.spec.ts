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

  test('M6-A-05 Stale revision rejected with 409, newer answer kept', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);
    const theory = exam.questions.find(q => q.type === 'Theory')!;

    const newer = await student.saveAnswer(theory.questionId, 'revision 3', undefined, { revision: 3, clientSavedAt: new Date().toISOString() });
    expect(newer.status()).toBe(200);
    expect((await newer.json()).revision).toBe(3);

    // An offline queue replaying an older edit late must not overwrite it.
    const stale = await student.saveAnswer(theory.questionId, 'revision 2', undefined, { revision: 2 });
    expect(stale.status()).toBe(409);
    const body = await stale.json();
    expect(body.code).toBe('STALE_REVISION');
    expect(body.currentRevision).toBe(3);

    // Replaying the same save (lost ack) is harmless.
    expect((await student.saveAnswer(theory.questionId, 'revision 3', undefined, { revision: 3 })).status()).toBe(200);

    const [row] = await teacher.results(exam.examId);
    expect(row.answers[0].answerText).toBe('revision 3');
  });

  test('M6-A-06 Submit returns a receipt; the saved answers endpoint returns it too', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    expect((await student.saveAnswer(theory.questionId, 'LIFO and FIFO', undefined, { revision: 1 })).status()).toBe(200);

    const before = await (await student.answers()).json();
    expect(before.status).toBe('InProgress');
    expect(before.answers).toEqual([expect.objectContaining({ questionId: theory.questionId, answerText: 'LIFO and FIFO', revision: 1 })]);
    expect(before.receipt).toBeNull();

    const submit = await student.submit();
    expect(submit.status()).toBe(200);
    const receipt = await submit.json();
    expect(receipt.answerCount).toBe(1);
    expect(receipt.sha256).toMatch(/^[0-9a-f]{64}$/);
    expect(receipt.submittedAt).toBeTruthy();

    const after = await (await student.answers()).json();
    expect(after.status).toBe('Submitted');
    expect(after.receipt.sha256).toBe(receipt.sha256);
  });

  test('M6-A-07 Another student cannot read or write my session', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 2);
    await students[0].start(exam.examId);
    await students[1].start(exam.examId);
    const theory = exam.questions.find(q => q.type === 'Theory')!;

    expect((await students[1].answers(students[0].sessionId)).status()).toBe(404);
    expect((await students[1].saveAnswer(theory.questionId, 'framed', students[0].sessionId)).status()).toBe(404);
  });

  test('M6-A-08 Answer to a question of another exam rejected', async ({ request }) => {
    const first = await examWithStudents(request, 1);
    const other = await examWithStudents(request, 1);
    await first.students[0].start(first.exam.examId);

    const res = await first.students[0].saveAnswer(other.exam.questions[0].questionId, 'planted');
    expect(res.status()).toBe(400);
    expect((await res.json()).message).toMatch(/does not belong/i);
  });
});
