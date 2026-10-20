import { test, expect } from '@playwright/test';
import { acceptDialogs, examWithStudents, signInWeb } from '../support/scenario';

test.describe('M11 — AI-Assisted Evaluation (results UI)', () => {
  test('M11-E-01 Results page shows AI suggestion', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(coding.questionId, 'int main(){ int max = arr[0]; for(int i=1;i<n;i++) if(arr[i]>max) max=arr[i]; return 0; }');
    await students[0].submit();

    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    const row = page.locator('tbody tr').filter({ hasText: users[0].name });
    await row.getByRole('button', { name: 'Review' }).click();

    await expect(page.getByText('AI Evaluation')).toBeVisible();
    await expect(page.getByText('Low Confidence')).toBeVisible();
    await expect(page.getByText('SUGGESTED MARK')).toBeVisible();
    await expect(page.getByText('18 / 20')).toBeVisible(); // 5/5 keywords → 0.9 × 20
    await expect(page.getByText(/teacher review required/i)).toBeVisible();
  });

  test('M11-E-02 Override in UI', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(theory.questionId, 'A stack is LIFO and a queue is FIFO.');
    await students[0].submit();

    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    const row = page.locator('tbody tr').filter({ hasText: users[0].name });
    await expect(row).toContainText('9/10');
    await row.getByRole('button', { name: 'Review' }).click();

    await page.locator('input[type="number"]').fill('7');
    await page.getByPlaceholder('e.g. Minor syntax error fixed (-1)').fill('Missing examples');
    const dialogs = acceptDialogs(page);
    await page.getByRole('button', { name: 'Save Grade Override' }).click();
    await expect.poll(() => dialogs.join('\n')).toContain('Grade override applied successfully');

    await expect(row).toContainText('7/10');
    const [result] = await teacher.results(exam.examId);
    expect(result.answers[0].teacherOverride).toEqual({ finalMarks: 7, note: 'Missing examples' });
    expect(result.answers[0].aiGrading.suggestedMarks).toBe(9); // AI suggestion kept for audit
  });

  test.fixme('M11-E-03 Plagiarism heat map', async ({ page, request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 3);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    for (const s of students) {
      await s.start(exam.examId);
      await s.saveAnswer(theory.questionId, 'identical answer text for the heat map');
      await s.submit();
    }
    await signInWeb(page, teacher);
    await page.goto(`/teacher/analytics/${exam.examId}`);
    await expect(page.getByTestId('plagiarism-heatmap').locator('[data-cell]')).toHaveCount(9);
  });

  test.fixme('M11-E-04 Test-case breakdown', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(coding.questionId, '#include <iostream>\nint main(){int n,x,m=-1e9;std::cin>>n;while(n--){std::cin>>x;if(x>m)m=x;}std::cout<<m;}');
    await students[0].submit();
    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    await page.locator('tbody tr').filter({ hasText: users[0].name }).getByRole('button', { name: 'Review' }).click();
    await expect(page.getByTestId('testcase-result')).toHaveCount(2);
    await expect(page.getByTestId('testcase-result').first()).toContainText(/pass/i);
  });
});
