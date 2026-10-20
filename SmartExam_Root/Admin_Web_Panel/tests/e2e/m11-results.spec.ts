import { test, expect } from '@playwright/test';
import { acceptDialogs, examWithStudents, signInWeb } from '../support/scenario';

// Coding answers are compiled and run in the background (Docker sandbox, or local MinGW where Docker is
// missing): the results page shows "Grading…" first and re-reads every few seconds.
test.describe.configure({ timeout: 90_000 });
const GRADED = { timeout: 60_000 };

test.describe('M11 — AI-Assisted Evaluation (results UI)', () => {
  test('M11-E-01 Results page shows AI suggestion', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    await students[0].start(exam.examId);
    // `arr` and `n` are never declared: a real compiler rejects it.
    await students[0].saveAnswer(coding.questionId, 'int main(){ int max = arr[0]; for(int i=1;i<n;i++) if(arr[i]>max) max=arr[i]; return 0; }');
    await students[0].submit();

    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    const row = page.locator('tbody tr').filter({ hasText: users[0].name });
    await row.getByRole('button', { name: 'Review' }).click();

    await expect(page.getByText('AI Evaluation')).toBeVisible();
    await expect(page.getByText('SUGGESTED MARK')).toBeVisible();
    await expect(page.getByText(/compilation failed/i)).toBeVisible(GRADED);
    await expect(page.getByText('High Confidence')).toBeVisible();
    await expect(page.getByText('0 / 20', { exact: true })).toBeVisible();
    await page.getByText('Compiler output', { exact: true }).click();
    await expect(page.getByText(/not declared/).last()).toBeVisible();
    await expect(page.getByTestId('testcase-result')).toHaveCount(2);
    await expect(page.getByTestId('testcase-result').first()).toContainText(/not run/i);
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
    await expect(row).toContainText('10/10'); // offline TF-IDF: same key terms as the model answer
    await row.getByRole('button', { name: 'Review' }).click();
    await expect(page.getByText(/similarity to the model answer/i)).toBeVisible();

    await page.locator('input[type="number"]').fill('7');
    await page.getByPlaceholder('e.g. Minor syntax error fixed (-1)').fill('Missing examples');
    const dialogs = acceptDialogs(page);
    await page.getByRole('button', { name: 'Save Grade Override' }).click();
    await expect.poll(() => dialogs.join('\n')).toContain('Grade override applied successfully');

    await expect(row).toContainText('7/10');
    await expect(page.getByText('Overridden')).toBeVisible();
    const [result] = await teacher.results(exam.examId);
    expect(result.answers[0].teacherOverride).toEqual({ finalMarks: 7, note: 'Missing examples' });
    expect(result.answers[0].aiGrading.suggestedMarks).toBe(10); // AI suggestion kept for audit
  });

  test('M11-E-03 Plagiarism heat map', async ({ page, request }) => {
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
    // Off-diagonal cells: identical answers are 100 % similar.
    await expect(page.getByTestId('plagiarism-heatmap').locator('[data-cell="0-1"]')).toHaveText('100');
  });

  test('M11-E-04 Test-case breakdown', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(coding.questionId, '#include <iostream>\nint main(){int n,x,m=-1e9;std::cin>>n;while(n--){std::cin>>x;if(x>m)m=x;}std::cout<<m;}');
    await students[0].submit();
    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    await page.locator('tbody tr').filter({ hasText: users[0].name }).getByRole('button', { name: 'Review' }).click();
    await expect(page.getByTestId('testcase-result')).toHaveCount(2);
    await expect(page.getByTestId('testcase-result').first()).toContainText(/pass/i, GRADED);
    await expect(page.getByTestId('testcase-result').nth(1)).toContainText(/hidden/i);
    await expect(page.getByText('20 / 20', { exact: true })).toBeVisible();
    await expect(page.getByTestId('code-analysis')).toContainText('O(n)');
  });

  test('M11-E-05 Plagiarism tab highlights the matched lines side by side', async ({ page, request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 2);
    const coding = exam.questions.find(q => q.type === 'Coding')!;
    const original = '#include <iostream>\nint main(){\n  int n, x, best = -1000000;\n  std::cin >> n;\n  while (n--) { std::cin >> x; if (x > best) best = x; }\n  std::cout << best;\n}';
    const renamed = original.replace(/best/g, 'top').replace(/\bx\b/g, 'value');
    await students[0].start(exam.examId);
    await students[0].saveAnswer(coding.questionId, original);
    await students[0].submit();
    await students[1].start(exam.examId);
    await students[1].saveAnswer(coding.questionId, renamed);
    await students[1].submit();

    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    await page.getByRole('button', { name: /plagiarism flags/i }).click();
    await expect(page.getByTestId('plagiarism-flag')).toHaveCount(1);
    await expect(page.getByTestId('plagiarism-flag')).toContainText('100% Similarity Match');
    await expect(page.getByTestId('plagiarism-pair').locator('[data-matched]').first()).toBeVisible();
    await expect(page.getByTestId('plagiarism-heatmap').locator('[data-cell]')).toHaveCount(4);
  });

  test('M11-E-06 Accept AI suggestions in bulk', async ({ page, request }) => {
    const { students, users, exam, teacher } = await examWithStudents(request, 1);
    const theory = exam.questions.find(q => q.type === 'Theory')!;
    const mcq = exam.questions.find(q => q.type === 'Mcq')!;
    await students[0].start(exam.examId);
    await students[0].saveAnswer(theory.questionId, 'A stack is LIFO.');
    await students[0].saveAnswer(mcq.questionId, '1');
    await students[0].submit();

    await signInWeb(page, teacher);
    await page.goto(`/teacher/results/${exam.examId}`);
    acceptDialogs(page);
    await page.getByRole('button', { name: 'Accept AI suggestions' }).click();
    await expect(page.getByText(/accepted 2 suggestion/i)).toBeVisible();

    const [result] = await teacher.results(exam.examId);
    for (const a of result.answers) expect(a.teacherOverride.finalMarks).toBe(a.aiGrading.suggestedMarks);
    await page.locator('tbody tr').filter({ hasText: users[0].name }).getByRole('button', { name: 'Review' }).click();
    await expect(page.getByText('Overridden')).toHaveCount(2);
  });
});
