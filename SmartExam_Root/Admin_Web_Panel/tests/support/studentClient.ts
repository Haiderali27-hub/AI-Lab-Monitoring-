import { expect, type APIRequestContext, type APIResponse } from '@playwright/test';
import { API_URL, uniqueTag } from './env';

/**
 * Simulates the WPF Student_Desktop_App over HTTP, mirroring exactly what
 * Student_Desktop_App/Services/ApiClient.cs and LiveMonitoringService.cs send:
 *   POST /api/auth/student-login   { usernameOrEmail, password, hardwareFingerprint, machineName, windowsUsername }
 *   GET  /api/exams/student/current                       (ApiEnvelope<StudentExamStatus>)
 *   POST /api/exams/student/start                         (desktop start path, ApiEnvelope<StartExamResult>)
 *   POST /api/exams/{id}/start-session                    (start path that also activates the exam)
 *   POST /api/exams/sessions/{sessionId}/save-answer      { questionId, answerText }
 *   POST /api/monitoring/heartbeat  { examSessionId, isForegroundExamApp, activeWindowTitle, processListSnapshot }
 *   POST /api/monitoring/event      { eventType, examSessionId, payloadJson }   (focus loss → "Violation")
 *   POST /api/exams/sessions/{sessionId}/submit
 *   GET  /api/notifications                               (teacher warnings / announcements)
 */
export class StudentClient {
  token = '';
  userId = '';
  sessionId = '';
  examId = '';

  constructor(
    readonly request: APIRequestContext,
    readonly email: string,
    readonly password: string,
    readonly hwid: string = `HWID-${uniqueTag('h')}`,
    readonly machineName: string = `LAB-PC-${uniqueTag('m').slice(-5).toUpperCase()}`,
    readonly windowsUsername = 'student',
  ) {}

  private headers() { return { Authorization: `Bearer ${this.token}` }; }

  // ── auth ────────────────────────────────────────────────────────────────
  static rawLogin(request: APIRequestContext, body: Record<string, unknown>): Promise<APIResponse> {
    return request.post(`${API_URL}/api/auth/student-login`, { data: body });
  }

  /** Student login with this client's HWID. Returns the raw response; on 200 the token is kept. */
  async login(hwid = this.hwid, machineName = this.machineName): Promise<APIResponse> {
    const res = await StudentClient.rawLogin(this.request, {
      usernameOrEmail: this.email,
      password: this.password,
      hardwareFingerprint: hwid,
      machineName,
      windowsUsername: this.windowsUsername,
    });
    if (res.status() === 200) {
      const body = await res.json();
      this.token = body.data.accessToken;
      this.userId = body.data.user.id;
    }
    return res;
  }

  /** login() that must succeed. */
  async signIn(): Promise<this> {
    const res = await this.login();
    expect(res.status(), await res.text()).toBe(200);
    return this;
  }

  me() { return this.request.get(`${API_URL}/api/auth/me`, { headers: this.headers() }); }
  logout() { return this.request.post(`${API_URL}/api/auth/logout`, { headers: this.headers(), data: {} }); }

  // ── exam dashboard ──────────────────────────────────────────────────────
  async currentExam(): Promise<any> {
    const res = await this.request.get(`${API_URL}/api/exams/student/current`, { headers: this.headers() });
    expect(res.status()).toBe(200);
    return (await res.json()).data;
  }

  examDetailRaw(examId: string) {
    return this.request.get(`${API_URL}/api/exams/${examId}`, { headers: this.headers() });
  }

  /** POST /api/exams/{examId}/start-session. On 200 remembers the session id. */
  async startSession(examId: string): Promise<APIResponse> {
    const res = await this.request.post(`${API_URL}/api/exams/${examId}/start-session`, { headers: this.headers(), data: {} });
    if (res.status() === 200) {
      this.sessionId = (await res.json()).sessionId;
      this.examId = examId;
    }
    return res;
  }

  /**
   * POST /api/exams/{examId}/readiness — what the desktop "Run check" button sends (Module 2).
   * Default = every desktop check passing; override items to simulate failures.
   */
  readiness(examId: string, overrides: { name: string; passed: boolean; detail?: string }[] = []): Promise<APIResponse> {
    const defaults = [
      { name: 'Network', passed: true, detail: '120 ms' },
      { name: 'ClockSkew', passed: true, detail: '2 s difference' },
      { name: 'ScreenCapture', passed: true, detail: 'Screen can be captured' },
      { name: 'Camera', passed: true, detail: 'Not required for this exam' },
      { name: 'Microphone', passed: true, detail: 'Not required for this exam' },
      { name: 'SecureMode', passed: true, detail: 'Single monitor, local session' },
      { name: 'Disk', passed: true, detail: '12.0 GB free' },
    ];
    const results = defaults.map(d => ({ ...d, ...overrides.find(o => o.name === d.name), durationMs: 10 }));
    return this.request.post(`${API_URL}/api/exams/${examId}/readiness`, {
      headers: this.headers(),
      data: { passedAll: results.every(r => r.passed), machineName: this.machineName, results },
    });
  }

  /** startSession() that must succeed. */
  async start(examId: string): Promise<string> {
    const res = await this.startSession(examId);
    expect(res.status(), await res.text()).toBe(200);
    return this.sessionId;
  }

  /** POST /api/exams/student/start — what the desktop client actually calls (ExamDashboard "Start"). */
  async startCurrent(): Promise<APIResponse> {
    const res = await this.request.post(`${API_URL}/api/exams/student/start`, { headers: this.headers(), data: {} });
    if (res.status() === 200) this.sessionId = (await res.json()).data.id;
    return res;
  }

  // ── answering / submission ──────────────────────────────────────────────
  saveAnswer(questionId: string, answerText: string, sessionId = this.sessionId) {
    return this.request.post(`${API_URL}/api/exams/sessions/${sessionId}/save-answer`, {
      headers: this.headers(),
      data: { questionId, answerText },
    });
  }

  submit(sessionId = this.sessionId) {
    return this.request.post(`${API_URL}/api/exams/sessions/${sessionId}/submit`, { headers: this.headers(), data: {} });
  }

  // ── live monitoring ─────────────────────────────────────────────────────
  heartbeat(opts: { isForegroundExamApp?: boolean; activeWindowTitle?: string; processListSnapshot?: string; sessionId?: string | null } = {}) {
    return this.request.post(`${API_URL}/api/monitoring/heartbeat`, {
      headers: this.headers(),
      data: {
        examSessionId: opts.sessionId === undefined ? (this.sessionId || null) : opts.sessionId,
        isForegroundExamApp: opts.isForegroundExamApp ?? true,
        activeWindowTitle: opts.activeWindowTitle ?? 'SmartExam - Exam Window',
        processListSnapshot: opts.processListSnapshot ?? 'SmartExam,code,explorer',
      },
    });
  }

  /** Generic monitoring event (the desktop client sends "Violation", "Attendance", "IdentityVerified"). */
  event(eventType: string, payload: unknown, sessionId: string | null = this.sessionId || null) {
    return this.request.post(`${API_URL}/api/monitoring/event`, {
      headers: this.headers(),
      data: { eventType, examSessionId: sessionId, payloadJson: JSON.stringify(payload) },
    });
  }

  /** Focus-loss violation exactly as LiveMonitoringService reports it. */
  violation(reason = 'FocusLoss', activeWindow = 'Google Chrome', sessionId: string | null = this.sessionId || null) {
    return this.event('Violation', { reason, activeWindow, atUtc: new Date().toISOString() }, sessionId);
  }

  async notifications(): Promise<{ unreadCount: number; notifications: any[] }> {
    const res = await this.request.get(`${API_URL}/api/notifications`, { headers: this.headers() });
    expect(res.status()).toBe(200);
    return res.json();
  }

  async violations(): Promise<{ totalViolations: number; violations: any[] }> {
    const res = await this.request.get(`${API_URL}/api/student/violations`, { headers: this.headers() });
    expect(res.status()).toBe(200);
    return res.json();
  }
}
