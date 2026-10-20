import { test, expect } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import { examWithStudents } from '../support/scenario';

test.describe('M11 — AI-Assisted Evaluation', () => {
  test('M11-A-01 Heuristic grading labelled honestly', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    const mcq = exam.questions.find(q => q.type === 'Mcq')!;
    await student.saveAnswer(coding.questionId, 'int main(){ int max=0; for(int i=0;i<n;i++) if(arr[i]>max) max=arr[i]; return 0; }');
    await student.saveAnswer(mcq.questionId, '1');
    expect((await student.submit()).status()).toBe(200);

    const [row] = await teacher.results(exam.examId);
    expect(row.status).toBe('Submitted');
    const codingAnswer = row.answers.find((a: any) => a.questionId === coding.questionId);
    expect(codingAnswer.aiGrading.confidence).toBe('Low');
    expect(codingAnswer.aiGrading.justification).toMatch(/teacher review required/i);
    expect(codingAnswer.aiGrading.justification).toMatch(/not compiled/i);

    // MCQ grading is deterministic and high confidence
    const mcqAnswer = row.answers.find((a: any) => a.questionId === mcq.questionId);
    expect(mcqAnswer.aiGrading.confidence).toBe('High');
    expect(mcqAnswer.aiGrading.suggestedMarks).toBe(mcq.marks);
  });

  test('M11-A-02 Plagiarism pair flagged', async ({ request }) => {
    const { students, exam, teacher, users } = await examWithStudents(request, 2);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    const copied = 'A stack follows LIFO order so the last pushed element is popped first while a queue follows FIFO order';
    for (const s of students) {
      await s.start(exam.examId);
      expect((await s.saveAnswer(theory.questionId, copied)).status()).toBe(200);
      expect((await s.submit()).status()).toBe(200);
    }

    const flags = await teacher.plagiarism(exam.examId);
    expect(flags).toHaveLength(1);
    expect([flags[0].studentAName, flags[0].studentBName].sort()).toEqual(users.map(u => u.name).sort());
    expect(flags[0].questionId).toBe(theory.questionId);
    expect(flags[0].similarityScore).toBeGreaterThanOrEqual(70);
  });
});

test.describe('M12 — Logs, Analytics & Audit Trail', () => {
  test('M12-A-01 Audit logs admin-only', async ({ request }) => {
    const teacher = await StaffApi.teacher(request);
    expect((await teacher.get('/api/audit-logs')).status()).toBe(403);
    const admin = await StaffApi.admin(request);
    expect((await admin.get('/api/audit-logs')).status()).toBe(200);
  });

  test('M12-A-02 Exam summary analytics', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 2);
    const mcq = exam.questions.find(q => q.type === 'Mcq')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(mcq.questionId, '1');   // correct
    await students[0].violation();
    await students[0].submit();
    await students[1].start(exam.examId);
    await students[1].saveAnswer(mcq.questionId, '0');   // wrong
    await students[1].submit();

    const res = await teacher.get(`/api/analytics/exams/${exam.examId}/summary`);
    expect(res.status()).toBe(200);
    const s = await res.json();
    expect(s.examId).toBe(exam.examId);
    expect(s.totalStudents).toBe(2);
    expect(s.totalMarks).toBe(35);
    expect(s.totalViolations).toBe(1);
    expect(s.studentsWithViolations).toBe(1);
    expect(s.questionStats).toHaveLength(3);
    expect(Object.values(s.scoreDistribution).reduce((a: number, b: any) => a + b, 0)).toBe(2);
  });

  test('M12-A-03 PDF report generate + download', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    await students[0].start(exam.examId);
    await students[0].submit();

    const gen = await teacher.post(`/api/analytics/exams/${exam.examId}/report`);
    expect(gen.status()).toBe(200);
    const { downloadUrl } = await gen.json();
    expect(downloadUrl).toMatch(/^\/api\/analytics\/reports\/.+\.pdf$/);

    const dl = await teacher.get(downloadUrl);
    expect(dl.status()).toBe(200);
    expect(dl.headers()['content-type']).toContain('application/pdf');
    expect((await dl.body()).subarray(0, 4).toString('latin1')).toBe('%PDF');

    const history = await (await teacher.get(`/api/analytics/exams/${exam.examId}/reports`)).json();
    expect(history.length).toBeGreaterThanOrEqual(1);
  });

  test('M12-A-04 Report download path traversal', async ({ request }) => {
    const teacher = await StaffApi.teacher(request);
    for (const name of ['..%2F..%2Fappsettings.json', '..%5C..%5Cappsettings.json', '%2E%2E%2Fappsettings.json', 'appsettings.json']) {
      const res = await teacher.get(`/api/analytics/reports/${name}`);
      expect([400, 404], name).toContain(res.status());
      expect(await res.text()).not.toContain('ConnectionStrings');
    }
  });
});
