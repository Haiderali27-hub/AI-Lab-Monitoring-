import { test, expect } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import { examWithStudents } from '../support/scenario';

// Module 2 — pre-exam readiness check API (IMPLEMENTATION_PLAN §3.2).
test.describe('M2 — Pre-Exam Verification & Mock Test', () => {
  test('M2-A-01 Readiness endpoint exists & is student-only', async ({ request }) => {
    const { exam, students } = await examWithStudents(request, 1, { requireReadiness: true });
    const teacher = await StaffApi.teacher(request);
    const body = { passedAll: true, machineName: 'PC-01', results: [{ name: 'Network', passed: true, detail: '120 ms', durationMs: 120 }] };

    // Teacher may not submit a readiness result
    expect((await teacher.post(`/api/exams/${exam.examId}/readiness`, body)).status()).toBe(403);

    // A student can; start then unlocks
    expect((await students[0].startSession(exam.examId)).status()).toBe(400);
    expect((await students[0].readiness(exam.examId)).status()).toBe(200);
    expect((await students[0].startSession(exam.examId)).status()).toBe(200);

    // Staff view shows the latest result
    const rows = await (await teacher.get(`/api/exams/${exam.examId}/readiness`)).json();
    expect(rows).toHaveLength(1);
    expect(rows[0].passedAll).toBe(true);
    expect(rows[0].results.map((r: any) => r.name)).toContain('SecureMode');
  });

  test('M2-A-02 Desktop start path returns READINESS_REQUIRED', async ({ request }) => {
    // A fresh student has only this exam, so it is their "current" one.
    const { exam, students } = await examWithStudents(request, 1, { requireReadiness: true });

    const current = await students[0].currentExam();
    expect(current.examId).toBe(exam.examId);
    expect(current.requireReadiness).toBe(true);
    expect(current.readinessPassed).toBe(false);

    const blocked = await students[0].startCurrent();
    expect(blocked.status()).toBe(400);
    expect((await blocked.json()).code).toBe('READINESS_REQUIRED');

    await students[0].readiness(exam.examId);
    expect((await students[0].currentExam()).readinessPassed).toBe(true);
    expect((await students[0].startCurrent()).status()).toBe(200);
  });
});
