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
  // Module 2 / 4 settings
  requireReadiness?: boolean;
  requireWebcam?: boolean;
  requireMic?: boolean;
  isMock?: boolean;
  lockdownMode?: 'Off' | 'Report' | 'Enforce';
  // Module 3 / 9 settings
  earlyJoinMinutes?: number;
  minAttendancePercent?: number;
  enforceSeat?: boolean;
  // Module 8 / 5 capture settings (mean seconds, 10–600)
  screenshotIntervalSeconds?: number;
  webcamIntervalSeconds?: number;
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
  static superAdmin(request: APIRequestContext) { return StaffApi.login(request, SEED.superAdmin.email, SEED.superAdmin.password); }

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
    return this.createUser('Student', label);
  }

  /** Any role an Admin may create (Student / Teacher / Invigilator); Admin roles need a SuperAdmin caller. */
  async createUser(role: string, label: string = role): Promise<CreatedUser> {
    const tag = uniqueTag(role.slice(0, 1).toLowerCase());
    const user = { name: `E2E ${label} ${tag}`, email: `${tag}@e2e.smartexam.test`, password: `${role}@123`, role };
    const res = await this.createUserRaw(user);
    expect(res.status(), await res.text()).toBe(201);
    const body = await res.json();
    return { ...user, userId: body.userId };
  }

  createInvigilator(label = 'Invigilator') { return this.createUser('Invigilator', label); }

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
      ...(input.requireReadiness !== undefined && { requireReadiness: input.requireReadiness }),
      ...(input.requireWebcam !== undefined && { requireWebcam: input.requireWebcam }),
      ...(input.requireMic !== undefined && { requireMic: input.requireMic }),
      ...(input.isMock !== undefined && { isMock: input.isMock }),
      ...(input.lockdownMode !== undefined && { lockdownMode: input.lockdownMode }),
      ...(input.earlyJoinMinutes !== undefined && { earlyJoinMinutes: input.earlyJoinMinutes }),
      ...(input.minAttendancePercent !== undefined && { minAttendancePercent: input.minAttendancePercent }),
      ...(input.enforceSeat !== undefined && { enforceSeat: input.enforceSeat }),
      ...(input.screenshotIntervalSeconds !== undefined && { screenshotIntervalSeconds: input.screenshotIntervalSeconds }),
      ...(input.webcamIntervalSeconds !== undefined && { webcamIntervalSeconds: input.webcamIntervalSeconds }),
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

  // ── Module 9: attendance, recalculation, seats ─────────────────────────────
  /** Multipart CSV upload exactly as the Eligibility page's "Upload Attendance" dialog sends it. */
  importAttendance(sectionId: string, csv: string): Promise<APIResponse> {
    return this.request.post(`${API_URL}/api/sections/${sectionId}/attendance/import`, {
      headers: this.headers(),
      multipart: { file: { name: 'attendance.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) } },
    });
  }

  recalculateEligibility(examId: string) { return this.post(`/api/exams/${examId}/eligibility/recalculate`); }

  assignSeat(examId: string, userId: string, workstationId: string | null) {
    return this.put(`/api/exams/${examId}/assignments/${userId}/workstation`, { workstationId });
  }

  async labs(): Promise<{ labId: string; name: string; workstations: { workstationId: string; machineNumber: string; hostName?: string | null; ipAddress: string }[] }[]> {
    const res = await this.get('/api/labs');
    expect(res.status()).toBe(200);
    return res.json();
  }

  /** Seeded demo seat (DbSeeder "Lab A (Demo)" → R1C1 / R1C2). */
  async workstation(machineNumber: string) {
    const ws = (await this.labs()).flatMap(l => l.workstations).find(w => w.machineNumber === machineNumber);
    expect(ws, `seeded workstation ${machineNumber}`).toBeTruthy();
    return ws!;
  }

  extendTime(examId: string, additionalMinutes: number) {
    return this.post(`/api/exams/${examId}/extend-time`, { additionalMinutes });
  }

  forceSubmitAll(examId: string) { return this.post(`/api/exams/${examId}/force-submit-all`); }

  // ── Module 7: proctor assignment (section teacher / admin) ───────────────
  assignProctor(examId: string, userId: string) { return this.post(`/api/exams/${examId}/proctors`, { userId }); }
  unassignProctor(examId: string, userId: string) { return this.delete(`/api/exams/${examId}/proctors/${userId}`); }

  async proctors(examId: string): Promise<{ userId: string; name: string; role: string }[]> {
    const res = await this.get(`/api/exams/${examId}/proctors`);
    expect(res.status()).toBe(200);
    return res.json();
  }

  // ── Module 5: monitoring evidence (owner teacher / assigned proctor / admin) ─
  screenshotsRaw(examId: string, sessionId: string) { return this.get(`/api/exams/${examId}/sessions/${sessionId}/screenshots`); }
  screenshotContent(screenshotId: string) { return this.get(`/api/screenshots/${screenshotId}/content`); }
  timelineRaw(examId: string, sessionId: string) { return this.get(`/api/exams/${examId}/sessions/${sessionId}/timeline`); }

  async screenshots(examId: string, sessionId: string): Promise<any[]> {
    const res = await this.screenshotsRaw(examId, sessionId);
    expect(res.status()).toBe(200);
    return res.json();
  }

  async timeline(examId: string, sessionId: string): Promise<any[]> {
    const res = await this.timelineRaw(examId, sessionId);
    expect(res.status()).toBe(200);
    return res.json();
  }

  // ── Module 10: proctoring (owner teacher / assigned proctor / admin) ─────
  liveRaw(examId: string) { return this.get(`/api/exams/${examId}/live`); }
  processesRaw(examId: string, sessionId: string) { return this.get(`/api/exams/${examId}/sessions/${sessionId}/processes`); }
  createIncidentRaw(sessionId: string, severity: string, notes: string) { return this.post('/api/incidents', { sessionId, severity, notes }); }
  updateIncidentRaw(incidentId: string, change: { status?: string; notes?: string }) { return this.patch(`/api/incidents/${incidentId}`, change); }
  incidentsRaw(examId: string, status?: string) { return this.get(`/api/exams/${examId}/incidents${status ? `?status=${status}` : ''}`); }

  async live(examId: string): Promise<{ examId: string; serverTimeUtc: string; students: any[] }> {
    const res = await this.liveRaw(examId);
    expect(res.status()).toBe(200);
    return res.json();
  }

  async incidents(examId: string, status?: string): Promise<any[]> {
    const res = await this.incidentsRaw(examId, status);
    expect(res.status()).toBe(200);
    return res.json();
  }

  // ── Module 7: academic structure (reads: staff; writes: Admin) ───────────
  createDepartment(name: string) { return this.post('/api/departments', { name }); }
  createCourse(deptId: string, name: string, code: string) { return this.post('/api/courses', { deptId, name, code }); }

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
