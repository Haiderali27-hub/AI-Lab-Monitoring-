import { test, expect } from '@playwright/test';
import { API_URL } from '../support/env';
import { StaffApi } from '../support/staffApi';
import { examWithStudents } from '../support/scenario';

test.describe('SEC — Security hardening', () => {
  // The E2E backend runs with RateLimiting:LoginPermitLimit=100000 so the suite can log in
  // freely; the 10/min limit is verified in-process by the xUnit suite instead.
  test.skip('SEC-A-01 Login rate limit (covered by xUnit — E2E lifts the limit)', async () => {});

  test('SEC-A-02 Unauthenticated access blocked', async ({ request }) => {
    for (const path of ['/api/users', '/api/exams', '/api/audit-logs']) {
      const res = await request.get(`${API_URL}${path}`);
      expect(res.status(), path).toBe(401);
    }
  });

  test('SEC-A-03 Student cannot call staff endpoints', async ({ request }) => {
    const { students, teacher } = await examWithStudents(request, 1);
    const auth = { Authorization: `Bearer ${students[0].token}` };

    expect((await request.get(`${API_URL}/api/users`, { headers: auth })).status()).toBe(403);
    const create = await request.post(`${API_URL}/api/exams`, { headers: auth, data: await teacher.examPayload() });
    expect(create.status()).toBe(403);
  });

  test('SEC-A-04 Logout revokes token', async ({ request }) => {
    const teacher = await StaffApi.teacher(request);
    expect((await teacher.get('/api/auth/me')).status()).toBe(200);
    expect((await teacher.post('/api/auth/logout')).status()).toBe(200);
    expect((await teacher.get('/api/auth/me')).status()).toBe(401);
  });

  test('SEC-A-05 Answer key hidden from students', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);

    // Sanity: staff do see the key, so the assertion below is meaningful
    const staffView = await teacher.examDetail(exam.examId);
    expect(staffView.questions.some((q: any) => q.correctOptionIndex === 1)).toBe(true);

    // D-42: before the student starts, the exam's details come without the paper.
    const before = await students[0].examDetailRaw(exam.examId);
    expect(before.status()).toBe(200);
    expect((await before.json()).questions).toHaveLength(0);

    await students[0].start(exam.examId);
    const res = await students[0].examDetailRaw(exam.examId);
    expect(res.status()).toBe(200);
    const detail = await res.json();
    expect(detail.questions).toHaveLength(3);
    for (const q of detail.questions) {
      expect(q.correctOptionIndex ?? null, `correctOptionIndex of ${q.type}`).toBeNull();
      expect(q.referenceAnswer ?? null, `referenceAnswer of ${q.type}`).toBeNull();
      for (const tc of q.testCases ?? []) expect(tc.isHidden).toBe(false);
    }
    const coding = detail.questions.find((q: any) => q.type === 'Coding');
    expect(coding.testCases).toHaveLength(1); // the hidden one is stripped
  });

  // Regression for the bug found while writing SEC-A-05: GET /api/exams/{id} used to serialise the
  // raw EF graph, including assignments[].student.passwordHash / salt, to any authenticated caller.
  test('SEC-A-06 Exam detail does not leak student password hashes or salts', async ({ request }) => {
    const { students, exam } = await examWithStudents(request, 2);
    const raw = await (await students[0].examDetailRaw(exam.examId)).text();
    expect(raw).not.toMatch(/"passwordHash"/i);
    expect(raw).not.toMatch(/"salt"/i);
  });
});
