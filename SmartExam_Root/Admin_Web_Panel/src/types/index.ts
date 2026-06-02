// ── Auth ──────────────────────────────────────────────────────
export type UserRole = 'SuperAdmin' | 'Admin' | 'Teacher' | 'Student';

export interface LoginRequest {
  email: string;
  password: string;
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
