import apiClient from './client';
import type {
  AttendanceImportResult, Exam, ExamAssignment, ExamProctor, PlagiarismFlag, PlagiarismMatrix, ReadinessResult, RecalculateResult,
  StudentResultRow,
} from '../types';

export const examsApi = {
  getAll: () =>
    apiClient.get<Exam[]>('/exams').then(r => r.data),

  getById: (id: string) =>
    apiClient.get<Exam>(`/exams/${id}`).then(r => r.data),

  getAssignments: (examId: string) =>
    apiClient.get<ExamAssignment[]>(`/exams/${examId}/assignments`).then(r => r.data),

  getReadiness: (examId: string) =>
    apiClient.get<ReadinessResult[]>(`/exams/${examId}/readiness`).then(r => r.data),

  updateEligibility: (examId: string, assignments: { userId: string; isEligible: boolean; eligibilityNote?: string; clearOverride?: boolean }[]) =>
    apiClient.put(`/exams/${examId}/eligibility`, { assignments }),

  // Module 9 — attendance-driven eligibility
  recalculateEligibility: (examId: string) =>
    apiClient.post<RecalculateResult>(`/exams/${examId}/eligibility/recalculate`).then(r => r.data),

  importAttendance: (sectionId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return apiClient.post<AttendanceImportResult>(`/sections/${sectionId}/attendance/import`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    }).then(r => r.data);
  },

  getResults: (examId: string) =>
    apiClient.get<StudentResultRow[]>(`/exams/${examId}/results`).then(r => r.data),

  getPlagiarism: (examId: string) =>
    apiClient.get<PlagiarismFlag[]>(`/exams/${examId}/plagiarism`).then(r => r.data),

  // Module 11 — class-wide similarity matrix (heat map), bulk accept, regrade one answer
  getPlagiarismMatrix: (examId: string, questionId?: string) =>
    apiClient.get<PlagiarismMatrix>(`/exams/${examId}/plagiarism/matrix`, { params: questionId ? { questionId } : undefined }).then(r => r.data),

  acceptAiSuggestions: (examId: string) =>
    apiClient.post<{ accepted: number; notReady: number; message: string }>(`/exams/${examId}/grading/accept-ai`).then(r => r.data),

  regradeAnswer: (answerId: string) =>
    apiClient.post<{ answerId: string; status: string; message: string }>(`/answers/${answerId}/regrade`).then(r => r.data),

  overrideGrade: (answerId: string, finalMarks: number, note: string) =>
    apiClient.post(`/answers/${answerId}/override`, { finalMarks, note }),

  forceSubmitAll: (examId: string) =>
    apiClient.post(`/exams/${examId}/force-submit-all`),

  getSections: () =>
    apiClient.get<{ sectionId: string; name: string; courseName: string }[]>('/exams/sections').then(r => r.data),

  getSectionStudents: (sectionId: string) =>
    apiClient.get<{ userId: string; name: string; email: string }[]>(`/exams/sections/${sectionId}/students`).then(r => r.data),

  create: (data: any) =>
    apiClient.post('/exams', data).then(r => r.data),

  update: (id: string, data: any) =>
    apiClient.put(`/exams/${id}`, data).then(r => r.data),

  delete: (id: string) =>
    apiClient.delete(`/exams/${id}`).then(r => r.data),

  // Module 9 — seating & roster management
  addStudent: (examId: string, userId: string) =>
    apiClient.post(`/exams/${examId}/assignments`, { userId }).then(r => r.data),

  removeStudent: (examId: string, userId: string) =>
    apiClient.delete(`/exams/${examId}/assignments/${userId}`).then(r => r.data),

  assignWorkstation: (examId: string, userId: string, workstationId: string | null) =>
    apiClient.put(`/exams/${examId}/assignments/${userId}/workstation`, { workstationId }).then(r => r.data),

  // Module 10 — proctor time extension
  extendTime: (examId: string, additionalMinutes: number) =>
    apiClient.post(`/exams/${examId}/extend-time`, { additionalMinutes }).then(r => r.data),

  // Module 7 — who supervises the exam besides its teacher (section teacher / admin manage this)
  getProctors: (examId: string) =>
    apiClient.get<ExamProctor[]>(`/exams/${examId}/proctors`).then(r => r.data),

  assignProctor: (examId: string, userId: string) =>
    apiClient.post<ExamProctor>(`/exams/${examId}/proctors`, { userId }).then(r => r.data),

  unassignProctor: (examId: string, userId: string) =>
    apiClient.delete(`/exams/${examId}/proctors/${userId}`),
};

