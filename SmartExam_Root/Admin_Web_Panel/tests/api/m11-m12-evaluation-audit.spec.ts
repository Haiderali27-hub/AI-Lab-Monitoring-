import { test, expect } from '@playwright/test';
import { StaffApi } from '../support/staffApi';
import { examWithStudents } from '../support/scenario';

test.describe('M11 — AI-Assisted Evaluation', () => {
  test('M11-A-01 Coding answer really compiled, result labelled with its engine', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const student = students[0];
    await student.start(exam.examId);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    const mcq = exam.questions.find(q => q.type === 'Mcq')!;
    // `n` and `arr` are never declared: the compiler rejects it.
    await student.saveAnswer(coding.questionId, 'int main(){ int max=0; for(int i=0;i<n;i++) if(arr[i]>max) max=arr[i]; return 0; }');
    await student.saveAnswer(mcq.questionId, '1');
    const submitStarted = Date.now();
    expect((await student.submit()).status()).toBe(200);
    expect(Date.now() - submitStarted, 'submit never waits for the compiler').toBeLessThan(5_000);

    const [row] = await teacher.gradedResults(exam.examId);
    expect(row.status).toBe('Submitted');
    const codingAnswer = row.answers.find((a: any) => a.questionId === coding.questionId);
    expect(codingAnswer.aiGrading.status).toBe('Graded');
    expect(codingAnswer.aiGrading.suggestedMarks).toBe(0);
    expect(codingAnswer.aiGrading.confidence).toBe('High');
    expect(codingAnswer.aiGrading.justification).toMatch(/compilation failed/i);
    expect(codingAnswer.aiGrading.engine).toMatch(/docker|mingw|g\+\+/i);
    expect(codingAnswer.aiGrading.compileOutput).toMatch(/not declared/);
    expect(codingAnswer.aiGrading.testCaseResults).toHaveLength(2);

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

  test('M11-A-03 Correct code passes visible and hidden cases; student sees only the visible one', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    const student = students[0];
    await student.start(exam.examId);
    await student.saveAnswer(coding.questionId, '#include <iostream>\nint main(){int n,x,m=-1000000;std::cin>>n;while(n--){std::cin>>x;if(x>m)m=x;}std::cout<<m;}');
    expect((await student.submit()).status()).toBe(200);

    const [row] = await teacher.gradedResults(exam.examId);
    const grading = row.answers.find((a: any) => a.questionId === coding.questionId).aiGrading;
    expect(grading.suggestedMarks).toBe(20);
    expect(grading.testCaseResults.map((r: any) => r.status)).toEqual(['Passed', 'Passed']);
    expect(grading.testCaseResults[1]).toMatchObject({ isHidden: true, expectedOutput: '-1' });
    expect(grading.analysis.complexitySignal).toBe('O(n)');

    expect((await teacher.forceSubmitAll(exam.examId)).status()).toBe(200);
    const res = await student.result(exam.examId);
    expect(res.status()).toBe(200);
    const body = await res.json();
    const feedback = body.answers.find((a: any) => a.questionType === 'Coding').aiFeedback;
    expect(feedback.testCaseResults).toHaveLength(1);
    expect(feedback.hiddenTotal).toBe(1);
    expect(feedback.hiddenPassed).toBe(1);
    expect(JSON.stringify(body)).not.toContain('-1 -7'); // the hidden input never reaches the student
  });

  test('M11-A-04 Plagiarism matrix is symmetric, owner-only, and refused for practice exams', async ({ request }) => {
    const { students, exam, teacher, admin } = await examWithStudents(request, 3);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    const texts = ['Stack is LIFO and queue is FIFO, both are linear.', 'Stack is LIFO and queue is FIFO, both are linear.', 'Trees are hierarchical structures with a root node.'];
    for (let i = 0; i < 3; i++) {
      await students[i].start(exam.examId);
      await students[i].saveAnswer(theory.questionId, texts[i]);
      await students[i].submit();
    }
    const res = await teacher.plagiarismMatrixRaw(exam.examId);
    expect(res.status()).toBe(200);
    const m = await res.json();
    expect(m.students).toHaveLength(3);
    for (let i = 0; i < 3; i++) {
      expect(m.matrix[i][i]).toBe(100);
      for (let j = 0; j < 3; j++) expect(m.matrix[i][j]).toBe(m.matrix[j][i]);
    }
    expect(m.matrix[0][1]).toBe(100);
    expect(m.matrix[0][2]).toBeLessThan(30);

    // D-13: another teacher sees nothing of this exam's grading.
    const other = await admin.createUser('Teacher', 'OtherTeacher');
    const otherApi = await StaffApi.login(request, other.email, other.password);
    expect((await otherApi.plagiarismMatrixRaw(exam.examId)).status()).toBe(403);
    expect((await otherApi.get(`/api/exams/${exam.examId}/results`)).status()).toBe(403);

    const practice = await teacher.createExam({ isMock: true });
    expect((await teacher.plagiarismMatrixRaw(practice.examId)).status()).toBe(400);
  });

  test('M11-A-05 Accept AI suggestions and regrade are audited', async ({ request }) => {
    const { students, exam, teacher, admin } = await examWithStudents(request, 1);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(theory.questionId, 'A stack is LIFO.');
    await students[0].submit();

    const [row] = await teacher.results(exam.examId);
    const answer = row.answers[0];
    const regrade = await teacher.post(`/api/answers/${answer.answerId}/regrade`);
    expect(regrade.status()).toBe(200);
    expect((await regrade.json()).status).toBe('Pending');
    const [again] = await teacher.gradedResults(exam.examId);
    expect(again.answers[0].aiGrading.suggestedMarks).toBe(answer.aiGrading.suggestedMarks);

    const accept = await teacher.post(`/api/exams/${exam.examId}/grading/accept-ai`);
    expect(accept.status()).toBe(200);
    expect((await accept.json()).accepted).toBe(1);
    const [final] = await teacher.results(exam.examId);
    expect(final.answers[0].teacherOverride.finalMarks).toBe(answer.aiGrading.suggestedMarks);

    expect((await admin.auditLogs('AI_SUGGESTIONS_ACCEPTED')).some((l: any) => l.entityId === exam.examId)).toBe(true);
    expect((await admin.auditLogs('AI_REGRADE_REQUESTED')).some((l: any) => l.entityId === answer.answerId)).toBe(true);
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
