import apiClient from './client';
import type { Exam, ExamAssignment } from '../types';

export const examsApi = {
  getAll: () =>
    apiClient.get<Exam[]>('/exams').then(r => r.data),

  getById: (id: string) =>
    apiClient.get<Exam>(`/exams/${id}`).then(r => r.data),

  getAssignments: (examId: string) =>
    apiClient.get<ExamAssignment[]>(`/exams/${examId}/assignments`).then(r => r.data),

  updateEligibility: (examId: string, assignments: { userId: string; isEligible: boolean; eligibilityNote?: string }[]) =>
    apiClient.put(`/exams/${examId}/eligibility`, { assignments }),

  getResults: (examId: string) =>
    apiClient.get<any[]>(`/exams/${examId}/results`).then(r => r.data),

  getPlagiarism: (examId: string) =>
    apiClient.get<any[]>(`/exams/${examId}/plagiarism`).then(r => r.data),

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
};

