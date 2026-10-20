import { test, expect } from '@playwright/test';
import { API_URL } from '../support/env';
import { StaffApi } from '../support/staffApi';
import { examWithStudents } from '../support/scenario';

// Module 2 (pre-exam readiness check) is not built yet — executable acceptance criteria.
// Planned API (IMPLEMENTATION_PLAN §8): POST /api/student/readiness (Student only).
test.describe('M2 — Pre-Exam Verification & Mock Test', () => {
  test.fixme('M2-A-01 Readiness endpoint exists & is student-only', async ({ request }) => {
    const { exam, students } = await examWithStudents(request, 1);
    const teacher = await StaffApi.teacher(request);
    const result = {
      examId: exam.examId,
      checks: [
        { name: 'Network', passed: true, detail: '120 ms' },
        { name: 'ClockSkew', passed: true, detail: '2 s' },
        { name: 'SecureMode', passed: true, detail: '1 monitor, no RDP' },
      ],
    };

    // Teacher may not submit a readiness result
    const asTeacher = await teacher.post('/api/student/readiness', result);
    expect(asTeacher.status()).toBe(403);

    // A student can, and the endpoint exists (not 404)
    const asStudent = await request.post(`${API_URL}/api/student/readiness`, {
      headers: { Authorization: `Bearer ${students[0].token}` },
      data: result,
    });
    expect(asStudent.status()).toBe(200);
  });
});
