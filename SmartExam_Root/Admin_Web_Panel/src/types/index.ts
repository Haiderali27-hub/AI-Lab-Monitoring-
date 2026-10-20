// ── Auth ──────────────────────────────────────────────────────
export type UserRole = 'SuperAdmin' | 'Admin' | 'Teacher' | 'Invigilator' | 'Student';

export interface LoginRequest {
  email: string;
  password: string;
  hwidHash?: string;
}

export interface LoginResponse {
  token: string;
  userId: string;
  name: string;
  email: string;
  role: UserRole;
  deviceBound: boolean;
  // Module 1: rotating refresh token + access-token expiry (POST /api/auth/refresh)
  refreshToken?: string | null;
  accessTokenExpiresAtUtc?: string | null;
}

export interface AuthUser {
  userId: string;
  name: string;
  email: string;
  role: UserRole;
}

// ── Users ──────────────────────────────────────────────────────
export interface User {
  userId: string;
  name: string;
  email: string;
  rollNumber?: string | null;       // Module 1: unique student roll number (login identifier)
  role: UserRole;
  isActive: boolean;
  createdAt: string;
  deviceBound: boolean;
  deviceRegisteredAt: string | null;
  machineName?: string | null;      // Windows computer name of the bound PC
  windowsUsername?: string | null;  // Windows account used on that PC
  deviceLastSeenAt?: string | null;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: UserRole;
  rollNumber?: string;
}

// ── Exams ──────────────────────────────────────────────────────
export type ExamStatus = 'Scheduled' | 'Active' | 'Ended';
export type QuestionType = 'Coding' | 'Theory' | 'Mcq';
export type SessionStatus = 'InProgress' | 'Submitted' | 'ForceSubmitted' | 'TimedOut';

export interface Exam {
  examId: string;
  title: string;
  courseName: string;
  sectionName: string;
  sectionId?: string;
  startTime: string;
  durationMinutes: number;
  status: ExamStatus;
  questionCount: number;
  isMock?: boolean;
  earlyJoinMinutes?: number;        // Module 3: start-session opens this many minutes early
  minAttendancePercent?: number;    // Module 9: threshold for "Recalculate from attendance"
  enforceSeat?: boolean;            // Module 9: only the assigned workstation may start/continue
  canSupervise?: boolean;           // Module 7: staff list — the caller may join this exam's live monitor
}

/** Module 7 — a staff member assigned to supervise an exam (GET /exams/{id}/proctors). */
export interface ExamProctor {
  examId: string;
  userId: string;
  name: string;
  email: string;
  role: UserRole;
  isActive: boolean;
  assignedAt: string;
  assignedBy: string;
}

// ── Academic structure (Module 7) ─────────────────────────────
export interface Department {
  deptId: string;
  name: string;
  courseCount: number;
}

export interface Course {
  courseId: string;
  deptId: string;
  departmentName: string;
  name: string;
  code: string;
  sectionCount: number;
}

/** Module 2 — latest pre-exam system check of one student (GET /exams/{id}/readiness). */
export interface ReadinessResult {
  userId: string;
  studentName: string;
  passedAll: boolean;
  checkedAt: string;
  isFresh: boolean;
  machineName?: string | null;
  results: { name: string; passed: boolean; detail?: string | null; durationMs: number }[];
}

export interface Question {
  questionId: string;
  type: QuestionType;
  bodyText: string;
  marks: number;
  orderIndex: number;
  testCases: TestCase[];
  optionsJson?: string | null;         // MCQ: JSON array of option strings
  correctOptionIndex?: number | null;  // MCQ answer key (teacher view only)
  referenceAnswer?: string | null;     // Coding/Theory sample solution (teacher view only)
}

export interface TestCase {
  testCaseId: string;
  input: string;
  expectedOutput: string;
  isHidden: boolean;
}

export interface ExamAssignment {
  assignmentId: string;
  examId: string;
  userId: string;
  studentName: string;
  studentEmail: string;
  workstationId: string | null;
  workstationNumber: string | null;
  workstationHostName?: string | null;
  workstationIp?: string | null;
  isEligible: boolean;
  eligibilityNote: string | null;
  // Module 9 — attendance (null when no record / no classes) and who made the decision
  attendancePercent?: number | null;
  attendedClasses?: number | null;
  totalClasses?: number | null;
  eligibilityOverridden?: boolean;
  eligibilitySource?: 'Attendance' | 'Manual' | null;
}

/** POST /sections/{id}/attendance/import response (Module 9). */
export interface AttendanceImportResult {
  imported: number;
  updated: number;
  errors: { line: number; email: string; reason: string }[];
  warnings: { line: number; email: string; reason: string }[];
}

/** POST /exams/{id}/eligibility/recalculate response (Module 9). */
export interface RecalculateResult {
  computed: number;
  ineligible: number;
  skippedOverridden: number;
  notComputed: number;
  minAttendancePercent: number;
}

// ── Labs / Seating (Module 9) ─────────────────────────────────
export interface LabWorkstation {
  workstationId: string;
  machineNumber: string;
  hostName?: string | null;             // real Windows PC name used for seat enforcement
  ipAddress: string;
  occupantName?: string | null;         // student most recently logged in from this PC
  occupantWindowsUser?: string | null;  // their Windows account
  occupantLastSeen?: string | null;
}

export interface LabInfo {
  labId: string;
  name: string;
  location: string;
  workstations: LabWorkstation[];
}

// ── Live Monitor ───────────────────────────────────────────────
export interface StudentLiveStatus {
  userId: string;
  studentName: string;
  workstationNumber: string;
  machineName?: string | null;      // real Windows PC name from the device binding
  windowsUsername?: string | null;  // Windows account in use on that PC
  sessionId: string;
  lastHeartbeat: string;
  activeWindow: string;
  answeredCount: number;
  totalQuestions: number;
  violationCount: number;
  tileStatus: TileStatus;
  lastViolation?: string | null;    // human label of the most recent violation, e.g. "Blocked keys: Alt+Tab"
  seatStatus?: 'Ok' | 'Mismatch' | 'Unassigned' | 'NotEnforced' | 'NotChecked' | null;  // Module 9
  seatDetail?: string | null;       // "logged in from R1C2 (assigned R1C1)" while mismatched
  sessionStatus?: SessionStatus | null;  // Module 6: InProgress until submitted / force-submitted / timed out
  extraMinutes?: number | null;          // Module 6: per-student extra time granted by a proctor
  // Module 5: liveness and what the PC reports
  isDisconnected?: boolean;              // no heartbeat for 30 s (server watchdog) until the next one arrives
  disconnectedAt?: string | null;
  idleSeconds?: number | null;           // seconds since the last keyboard/mouse input on the PC
  cameraStatus?: CameraStatus | null;    // on-device webcam monitoring (Off when the exam needs no webcam)
  processes?: string | null;             // apps with a visible window, comma-separated
  unlockedUntil?: string | null;         // Module 10: a proctor suspended this PC's lockdown until then (server time)
}

/** Module 10: one student on GET /exams/{id}/live (the board a page starts from before live hub events arrive). */
export interface LiveSnapshotStudent {
  userId: string;
  studentName: string;
  workstationNumber: string;
  machineName?: string | null;
  windowsUsername?: string | null;
  sessionId: string;
  sessionStatus: SessionStatus;
  lastHeartbeatAt?: string | null;
  isDisconnected: boolean;
  disconnectedAt?: string | null;
  extraMinutes: number;
  unlockedUntilUtc?: string | null;
  answeredCount: number;
  totalQuestions: number;
  violationCount: number;
  tileStatus: TileStatus;
  lastViolationType?: string | null;
  lastViolationPayload?: string | null;
  activeWindow?: string | null;
  isForegroundExamApp?: boolean | null;
  processes?: string | null;
  idleSeconds?: number | null;
  cameraStatus?: CameraStatus | null;
  latestScreenshot?: { screenshotId: string; capturedAt: string; trigger: string } | null;
  openIncidents: number;
}

export interface LiveSnapshot {
  examId: string;
  serverTimeUtc: string;
  students: LiveSnapshotStudent[];
}

/** Module 10: apps the PC reported during a session (GET /exams/{id}/sessions/{sid}/processes). */
export interface SessionProcesses {
  current: string[];
  currentAt?: string | null;
  seen: { name: string; firstSeenAt: string; lastSeenAt: string; running: boolean }[];
}

export type IncidentStatus = 'Open' | 'Escalated' | 'Resolved';
export type IncidentSeverity = 'Low' | 'Medium' | 'High' | 'Critical';

/** Module 10: a proctor incident (POST/PATCH /incidents, GET /exams/{id}/incidents). */
export interface Incident {
  incidentId: string;
  examId: string;
  sessionId: string;
  userId: string;
  studentName: string;
  severity: IncidentSeverity;
  status: IncidentStatus;
  notes: string;
  statusNote?: string | null;
  createdBy: string;
  createdByName?: string | null;
  createdAt: string;
  updatedAt: string;
  resolvedAt?: string | null;
  resolvedBy?: string | null;
  resolvedByName?: string | null;
}

// Picture state (Covered / Frozen / Unavailable) or, Module 5 S8, the desktop's on-device face / phone models.
export type CameraStatus = 'Off' | 'Ok' | 'Covered' | 'Frozen' | 'Unavailable' | 'NoFace' | 'MultipleFaces' | 'LookingAway' | 'Phone';

/** Module 5: one capture (screen or webcam) of a session, GET /exams/{id}/sessions/{sid}/screenshots. */
export interface ScreenshotSummary {
  screenshotId: string;
  kind: 'Screen' | 'Webcam';
  trigger: 'Interval' | 'Violation' | 'Manual';
  triggerReason?: string | null;
  capturedAt: string;
  uploadedAt: string;
  sizeBytes: number;
  sha256: string;
  contentUrl: string;
}

/** Module 5: one entry of a session's activity timeline, newest first. */
export interface SessionTimelineItem {
  at: string;
  kind: 'Event' | 'Screenshot' | 'Warning' | 'Incident' | 'Action' | 'Status';
  type: string;
  severity: 'Info' | 'Warning' | 'Suspicious' | 'Violation';
  label: string;
  detail?: string | null;                // payload JSON, violation reason of a capture, warning text, incident notes, action JSON
  screenshotId?: string | null;
  receivedAt?: string | null;            // server receive time (events, captures); later than `at` for a replayed event
}

export type TileStatus = 'Normal' | 'Warning' | 'Suspicious' | 'Violation';

// ── Results ────────────────────────────────────────────────────
// Module 11: grading is asynchronous for coding answers — Pending until the sandbox has run every test case.
export type GradingStatus = 'Pending' | 'Graded' | 'Failed';
export type TestCaseStatus = 'Pending' | 'Passed' | 'Failed' | 'TimedOut' | 'MemoryExceeded' | 'RuntimeError' | 'NotRun';

export interface TestCaseResultView {
  testCaseId: string | null;
  name: string;
  input: string;
  expectedOutput: string;
  actualOutput: string | null;
  isHidden: boolean;
  isEdgeCase: boolean;
  category: string | null;
  weight: number;
  status: TestCaseStatus;
  passed: boolean;
  exitCode: number | null;
  durationMs: number | null;
  compareMode: string | null;
}

/** Static analysis of a coding answer (teacher only): signals, not verdicts. */
export interface CodeAnalysisView {
  loopCount: number;
  maxLoopNesting: number;
  complexitySignal: string;
  usesRecursion: boolean;
  recursiveFunctions: string[];
  ifCount: number;
  numericLiteralCount: number;
  hardcodingSignals: string[];
  hardcodingSuspicion: 'Low' | 'Medium' | 'High';
  passRate: number | null;
  nearCorrect: boolean;
  categories: { category: string; passed: number; total: number }[];
}

export interface AiGradingView {
  suggestedMarks: number;
  justification: string;
  confidence: 'High' | 'Medium' | 'Low';
  status?: GradingStatus;
  engine?: string | null;
  compileOutput?: string | null;
  durationMs?: number | null;
  gradedAt?: string | null;
  llmSummary?: string | null;
  analysis?: CodeAnalysisView | null;
  testCaseResults?: TestCaseResultView[];
}

export interface AnswerResult {
  answerId: string;
  questionId: string;
  questionText: string;
  questionType: QuestionType;
  marks: number;
  answerText: string;
  aiGrading: AiGradingView | null;
  teacherOverride: {
    finalMarks: number;
    note: string | null;
  } | null;
}

export interface StudentResultRow {
  userId: string;
  studentName: string;
  studentEmail: string;
  sessionId: string;
  status: string;
  submittedAt: string | null;
  receiptSha256?: string | null;     // Module 6: SHA-256 receipt the student got on submit
  receiptAnswerCount?: number | null;
  extraMinutes?: number;
  answers: AnswerResult[];
}

/** One matched region of a plagiarism pair: 1-based inclusive line ranges in each answer, plus the excerpts. */
export interface MatchedSegment {
  aStartLine?: number;
  aEndLine?: number;
  bStartLine?: number;
  bEndLine?: number;
  A: string;
  B: string;
}

export interface PlagiarismFlag {
  plagId: string;
  questionId: string;
  questionText: string;
  questionType?: QuestionType;
  userIdA?: string;
  userIdB?: string;
  studentAName: string;
  studentBName: string;
  similarityScore: number;
  matchingSegments?: string; // JSON string: MatchedSegment[]
}

/** GET /exams/{id}/plagiarism/matrix — symmetric similarity (0–100), students in submission order. */
export interface PlagiarismMatrix {
  examId: string;
  questionId: string | null;
  students: { userId: string; name: string; submittedAt: string | null }[];
  questions: { questionId: string; type: QuestionType; orderIndex: number; bodyText: string }[];
  matrix: number[][];
  threshold: number;
}

// ── Analytics ─────────────────────────────────────────────────────────────────

export interface ExamSummaryAnalytics {
  examId: string;
  examTitle: string;
  totalStudents: number;
  totalMarks: number;
  averageScore: number;
  highestScore: number;
  lowestScore: number;
  passRate: number;
  scoreDistribution: Record<string, number>; // { "0-20": 3, "21-40": 5, ... }
  questionStats: QuestionStat[];
  totalViolations: number;
  studentsWithViolations: number;
}

export interface QuestionStat {
  questionId: string;
  shortLabel: string;    // "Q1", "Q2"
  fullText: string;      // truncated question text
  totalMarks: number;
  averageMarksEarned: number;
  difficultyPercent: number; // higher = easier (students scored well)
}

export interface SystemAnalytics {
  totalStudents: number;
  totalTeachers: number;
  totalExams: number;
  totalExamsLast30Days: number;
  totalViolationsLast30Days: number;
  examsByMonth: { label: string; count: number }[];
  topCoursesByExamCount: { label: string; count: number }[];
  violationTrendLast30Days: { date: string; count: number }[];
}

export interface ExamReport {
  reportId: string;
  reportType: string;
  generatedAt: string;
  fileName: string;
}

// ── Student Portal ─────────────────────────────────────────────────────────────

export interface StudentDashboard {
  totalExamsTaken: number;
  averageScore: number;
  examsPassed: number;
  totalViolations: number;
  recentExams: StudentExamSummary[];
  performanceTrend: { title: string; scorePercent: number; startedAt: string }[];
}

export interface StudentExamSummary {
  sessionId: string;
  examId: string;
  title: string;
  courseName: string;
  startedAt: string;
  submittedAt: string | null;
  status: string;
  totalMarks: number;
  earnedMarks: number;
  scorePercent: number;
  violationCount: number;
  passed: boolean;
}

export interface StudentExamResult {
  sessionId: string;
  title: string;
  startedAt: string;
  submittedAt: string | null;
  status: string;
  receiptSha256?: string | null;      // Module 6: SHA-256 submission receipt (same as on submit)
  receiptAnswerCount?: number | null;
  totalMarks: number;
  earnedMarks: number;
  scorePercent: number;
  passed: boolean;
  answers: StudentAnswer[];
}

export interface StudentAnswer {
  answerId: string;
  questionText: string;
  questionType: string;
  totalMarks: number;
  answerText: string;
  earnedMarks: number;
  aiFeedback: {
    suggestedMarks: number;
    justification: string;
    confidence: 'High' | 'Medium' | 'Low';
  } | null;
  teacherOverridden: boolean;
}

// ── Notifications ──────────────────────────────────────────────────────────────

export interface AppNotification {
  notificationId: string;
  title: string;
  body: string;
  type: string;
  isRead: boolean;
  createdAt: string;
  relatedEntityId: string | null;
}

export interface NotificationsResponse {
  unreadCount: number;
  notifications: AppNotification[];
}

// ── Toast ──────────────────────────────────────────────────────────────────────

export interface Toast {
  id: string;
  title: string;
  message: string;
  type: 'success' | 'warning' | 'info' | 'error';
}

