import { expect, type APIRequestContext, type Page } from '@playwright/test';
import { StaffApi, type CreatedExam, type CreatedUser, type ExamInput } from './staffApi';
import { StudentClient } from './studentClient';
import { API_URL } from './env';

export interface ExamScenario {
  admin: StaffApi;
  teacher: StaffApi;
  exam: CreatedExam;
  users: CreatedUser[];
  students: StudentClient[];
}

/**
 * Fresh, isolated test data: `count` brand-new students (created by the admin), one exam owned
 * by the seeded teacher with those students assigned, and a logged-in desktop-client
 * simulator per student (each with its own HWID).
 */
export async function examWithStudents(request: APIRequestContext, count = 1, examInput: ExamInput = {}): Promise<ExamScenario> {
  const admin = await StaffApi.admin(request);
  const teacher = await StaffApi.teacher(request);
  const users: CreatedUser[] = [];
  for (let i = 0; i < count; i++) users.push(await admin.createStudent(`Student${i + 1}`));
  const exam = await teacher.createExam({ ...examInput, studentIds: users.map(u => u.userId) });
  const students: StudentClient[] = [];
  for (const u of users) students.push(await new StudentClient(request, u.email, u.password).signIn());
  return { admin, teacher, exam, users, students };
}

/** Log the browser in by seeding the same localStorage keys the LoginPage writes (skips the form). */
export async function signInWeb(page: Page, who: StaffApi | { token: string; userId: string; name: string; email: string; role: string }) {
  const user = who instanceof StaffApi ? who.webUser : { userId: who.userId, name: who.name, email: who.email, role: who.role };
  const token = who.token;
  await page.addInitScript(([t, u]) => {
    if (!window.localStorage.getItem('smartexam_token')) {
      window.localStorage.setItem('smartexam_token', t);
      window.localStorage.setItem('smartexam_user', u);
    }
  }, [token, JSON.stringify(user)] as const);
}

/** Log a student into the WEB portal (staff-style /api/auth/login, no HWID). */
export async function signInStudentWeb(page: Page, request: APIRequestContext, user: CreatedUser) {
  const res = await request.post(`${API_URL}/api/auth/login`, { data: { email: user.email, password: user.password } });
  expect(res.status()).toBe(200);
  const body = await res.json();
  await signInWeb(page, { token: body.token, userId: body.userId, name: body.name, email: body.email, role: body.role });
}

/** Accept every alert/confirm, answering prompts with `promptValue`. Returns the messages seen. */
export function acceptDialogs(page: Page, promptValue = ''): string[] {
  const seen: string[] = [];
  page.on('dialog', async dialog => {
    seen.push(dialog.message());
    if (dialog.type() === 'prompt') await dialog.accept(promptValue);
    else await dialog.accept();
  });
  return seen;
}
