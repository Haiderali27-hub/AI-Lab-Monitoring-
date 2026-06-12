// ── Auth ──────────────────────────────────────────────────────
export type UserRole = 'SuperAdmin' | 'Admin' | 'Teacher' | 'Student';

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
  role: UserRole;
  isActive: boolean;
  createdAt: string;
  deviceBound: boolean;
  deviceRegisteredAt: string | null;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: UserRole;
}

// ── Exams ──────────────────────────────────────────────────────
export type ExamStatus = 'Scheduled' | 'Active' | 'Ended';
export type QuestionType = 'Coding' | 'Theory';
export type SessionStatus = 'InProgress' | 'Submitted' | 'ForceSubmitted' | 'TimedOut';

export interface Exam {
  examId: string;
  title: string;
  courseName: string;
  sectionName: string;
  startTime: string;
  durationMinutes: number;
  status: ExamStatus;
  questionCount: number;
}

export interface Question {
  questionId: string;
  type: QuestionType;
  bodyText: string;
  marks: number;
  orderIndex: number;
  testCases: TestCase[];
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
  workstationNumber: string | null;
  isEligible: boolean;
  eligibilityNote: string | null;
}

// ── Live Monitor ───────────────────────────────────────────────
export interface StudentLiveStatus {
  userId: string;
  studentName: string;
  workstationNumber: string;
  sessionId: string;
  lastHeartbeat: string;
  activeWindow: string;
  answeredCount: number;
  totalQuestions: number;
  violationCount: number;
  tileStatus: 'Normal' | 'Warning' | 'Violation';
}

// ── Results ────────────────────────────────────────────────────
export interface AnswerResult {
  answerId: string;
  questionId: string;
  questionText: string;
  questionType: QuestionType;
  marks: number;
  answerText: string;
  aiGrading: {
    suggestedMarks: number;
    justification: string;
    confidence: 'High' | 'Medium' | 'Low';
  } | null;
  teacherOverride: {
    finalMarks: number;
    note: string;
  } | null;
}

export interface PlagiarismFlag {
  plagId: string;
  questionId: string;
  questionText: string;
  studentAName: string;
  studentBName: string;
  similarityScore: number;
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

