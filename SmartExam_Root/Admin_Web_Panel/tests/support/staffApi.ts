import { expect, type APIRequestContext, type APIResponse } from '@playwright/test';
import { API_URL, SEED, uniqueTag } from './env';

export type QuestionType = 'Coding' | 'Theory' | 'Mcq';

export interface QuestionInput {
  type: QuestionType;
  bodyText: string;
  marks: number;
  options?: string[] | null;
  correctOptionIndex?: number | null;
  referenceAnswer?: string | null;
  testCases?: { input: string; expectedOutput: string; isHidden: boolean }[];
}

export interface ExamInput {
  title?: string;
  sectionId?: string;
  startTime?: Date;
  durationMinutes?: number;
  allowedApps?: string;
  aiEvaluationEnabled?: boolean;
  plagiarismThreshold?: number;
  questions?: QuestionInput[];
  studentIds?: string[];
  instructions?: string;
}

export interface CreatedUser {
  userId: string;
  name: string;
  email: string;
  password: string;
  role: string;
}

export interface CreatedExam {
  examId: string;
  title: string;
  /** Questions as stored by the server (ids, type), in order. */
  questions: { questionId: string; type: QuestionType; marks: number }[];
}

/** One MCQ (correct = index 1), one theory and one coding question (one hidden test case). */
export function defaultQuestions(): QuestionInput[] {
  return [
    {
      type: 'Mcq',
      bodyText: 'Which data structure is LIFO?',
      marks: 5,
      options: ['Queue', 'Stack', 'Heap'],
      correctOptionIndex: 1,
    },
    {
      type: 'Theory',
      bodyText: 'Explain the difference between a stack and a queue.',
      marks: 10,
      referenceAnswer: 'Stack is LIFO, queue is FIFO.',
    },
    {
      type: 'Coding',
      bodyText: 'Read n numbers and print the maximum.',
      marks: 20,
      referenceAnswer: 'int main(){ /* secret reference solution */ }',
      testCases: [
        { input: '3\n1 5 2', expectedOutput: '5', isHidden: false },
        { input: '2\n-1 -7', expectedOutput: '-1', isHidden: true },
      ],
    },
  ];
}

/**
 * Thin HTTP client for staff (Admin / Teacher) actions, used by tests to create their OWN
 * users and exams instead of depending on shared seed state.
 */
export class StaffApi {
  private constructor(
    readonly request: APIRequestContext,
    readonly token: string,
    readonly userId: string,
    readonly name: string,
    readonly role: string,
  ) {}

  static async login(request: APIRequestContext, email: string, password: string): Promise<StaffApi> {
    const res = await request.post(`${API_URL}/api/auth/login`, { data: { email, password } });
    expect(res.status(), `staff login for ${email}`).toBe(200);
    const body = await res.json();
    return new StaffApi(request, body.token, body.userId, body.name, body.role);
  }

  static admin(request: APIRequestContext) { return StaffApi.login(request, SEED.admin.email, SEED.admin.password); }
  static teacher(request: APIRequestContext) { return StaffApi.login(request, SEED.teacher.email, SEED.teacher.password); }

  /** The login payload the web panel stores in localStorage (see AuthContext). */
  get webUser() {
    return { userId: this.userId, name: this.name, email: '', role: this.role };
  }

  // ── raw verbs ────────────────────────────────────────────────────────────
  private headers() { return { Authorization: `Bearer ${this.token}` }; }
  get(path: string) { return this.request.get(`${API_URL}${path}`, { headers: this.headers() }); }
  post(path: string, data?: unknown) { return this.request.post(`${API_URL}${path}`, { headers: this.headers(), data: data ?? {} }); }
  put(path: string, data?: unknown) { return this.request.put(`${API_URL}${path}`, { headers: this.headers(), data: data ?? {} }); }
  patch(path: string, data?: unknown) { return this.request.patch(`${API_URL}${path}`, { headers: this.headers(), data: data ?? {} }); }
  delete(path: string) { return this.request.delete(`${API_URL}${path}`, { headers: this.headers() }); }

  // ── users (Admin) ────────────────────────────────────────────────────────
  createUserRaw(user: { name: string; email: string; password: string; role: string }): Promise<APIResponse> {
    return this.post('/api/users', user);
  }

  async createStudent(label = 'Student'): Promise<CreatedUser> {
    const tag = uniqueTag('s');
    const user = { name: `E2E ${label} ${tag}`, email: `${tag}@e2e.smartexam.test`, password: 'Student@123', role: 'Student' };
    const res = await this.createUserRaw(user);
    expect(res.status(), await res.text()).toBe(201);
    const body = await res.json();
    return { ...user, userId: body.userId };
  }

  resetDeviceBinding(userId: string) { return this.delete(`/api/users/${userId}/device-binding`); }
  deactivate(userId: string) { return this.patch(`/api/users/${userId}/deactivate`); }

  async auditLogs(eventType?: string): Promise<any[]> {
    const res = await this.get(`/api/audit-logs?take=1000${eventType ? `&eventType=${eventType}` : ''}`);
    expect(res.status()).toBe(200);
    return res.json();
  }

  // ── exams (Teacher) ──────────────────────────────────────────────────────
  async sections(): Promise<{ sectionId: string; name: string; courseName: string }[]> {
    const res = await this.get('/api/exams/sections');
    expect(res.status()).toBe(200);
    return res.json();
  }

  async examPayload(input: ExamInput = {}) {
    const sectionId = input.sectionId ?? (await this.sections())[0].sectionId;
    return {
      title: input.title ?? `E2E Exam ${uniqueTag('x')}`,
      instructions: input.instructions ?? 'Automated test exam.',
      sectionId,
      startTime: (input.startTime ?? new Date(Date.now() - 60_000)).toISOString(),
      durationMinutes: input.durationMinutes ?? 60,
      allowedApps: input.allowedApps ?? '["code.exe"]',
      aiEvaluationEnabled: input.aiEvaluationEnabled ?? true,
      plagiarismThreshold: input.plagiarismThreshold ?? 70,
      questions: input.questions ?? defaultQuestions(),
      studentIds: input.studentIds ?? [],
    };
  }

  async createExamRaw(input: ExamInput = {}): Promise<APIResponse> {
    return this.post('/api/exams', await this.examPayload(input));
  }

  /** Creates an exam whose window is open right now (started 1 min ago, 60 min long) unless overridden. */
  async createExam(input: ExamInput = {}): Promise<CreatedExam> {
    const res = await this.createExamRaw(input);
    expect(res.status(), await res.text()).toBe(201);
    const { examId, title } = await res.json();
    const detail = await this.examDetail(examId);
    const questions = [...detail.questions]
      .sort((a: any, b: any) => a.orderIndex - b.orderIndex)
      .map((q: any) => ({ questionId: q.questionId, type: q.type, marks: q.marks }));
    return { examId, title, questions };
  }

  async examDetail(examId: string): Promise<any> {
    const res = await this.get(`/api/exams/${examId}`);
    expect(res.status()).toBe(200);
    return res.json();
  }

  async exams(): Promise<any[]> {
    const res = await this.get('/api/exams');
    expect(res.status()).toBe(200);
    return res.json();
  }

  assign(examId: string, userId: string) { return this.post(`/api/exams/${examId}/assignments`, { userId }); }

  async assignments(examId: string): Promise<any[]> {
    const res = await this.get(`/api/exams/${examId}/assignments`);
    expect(res.status()).toBe(200);
    return res.json();
  }

  setEligibility(examId: string, userId: string, isEligible: boolean, eligibilityNote = '') {
    return this.put(`/api/exams/${examId}/eligibility`, { assignments: [{ userId, isEligible, eligibilityNote }] });
  }

  extendTime(examId: string, additionalMinutes: number) {
    return this.post(`/api/exams/${examId}/extend-time`, { additionalMinutes });
  }

  forceSubmitAll(examId: string) { return this.post(`/api/exams/${examId}/force-submit-all`); }

  async results(examId: string): Promise<any[]> {
    const res = await this.get(`/api/exams/${examId}/results`);
    expect(res.status()).toBe(200);
    return res.json();
  }

  async plagiarism(examId: string): Promise<any[]> {
    const res = await this.get(`/api/exams/${examId}/plagiarism`);
    expect(res.status()).toBe(200);
    return res.json();
  }

  /**
   * The Live Monitor page shows the FIRST exam with status Active, so monitoring tests end
   * every other Active exam first (force-submit-all) to make the page deterministic.
   */
  async endAllActiveExams(): Promise<void> {
    for (const exam of await this.exams()) {
      if (exam.status === 'Active') {
        const res = await this.forceSubmitAll(exam.examId);
        expect(res.status()).toBe(200);
      }
    }
  }
}
