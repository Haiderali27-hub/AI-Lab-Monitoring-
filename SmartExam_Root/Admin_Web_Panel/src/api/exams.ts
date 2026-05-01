import type {
  CreateExamPayload,
  ExamAssignmentDetail,
  ExamCandidate,
  ExamSummary,
  LiveRosterItem,
  Proctor,
  UpdateExamPayload,
} from '../types'
import { apiClient, unwrap } from './client'

export async function getLiveRoster(): Promise<LiveRosterItem[]> {
  return unwrap<LiveRosterItem[]>(apiClient.get('/api/exams/admin/live-roster'))
}

export async function getExams(): Promise<ExamSummary[]> {
  return unwrap<ExamSummary[]>(apiClient.get('/api/exams'))
}

export async function getExamCandidates(): Promise<ExamCandidate[]> {
  return unwrap<ExamCandidate[]>(apiClient.get('/api/exams/candidates'))
}

export async function getExamProctors(): Promise<Proctor[]> {
  return unwrap<Proctor[]>(apiClient.get('/api/exams/proctors'))
}

export async function createExam(payload: CreateExamPayload): Promise<{ id: string }> {
  return unwrap<{ id: string }>(apiClient.post('/api/exams', payload))
}

export async function updateExam(examId: string, payload: UpdateExamPayload): Promise<{ id: string }> {
  return unwrap<{ id: string }>(apiClient.put(`/api/exams/${examId}`, payload))
}

export async function deleteExam(examId: string): Promise<void> {
  await unwrap<object>(apiClient.delete(`/api/exams/${examId}`))
}

export async function cancelExam(examId: string): Promise<void> {
  await unwrap<{ id: string }>(apiClient.patch(`/api/exams/${examId}/cancel`))
}

export async function archiveExam(examId: string, isArchived: boolean): Promise<void> {
  await unwrap<{ id: string; isArchived: boolean }>(
    apiClient.patch(`/api/exams/${examId}/archive`, { isArchived }),
  )
}

export async function getExamAssignments(examId: string): Promise<ExamAssignmentDetail[]> {
  return unwrap<ExamAssignmentDetail[]>(apiClient.get(`/api/exams/${examId}/assignments`))
}

export async function updateExamAssignments(examId: string, assignments: CreateExamPayload['assignments']): Promise<void> {
  await unwrap<{ examId: string }>(apiClient.put(`/api/exams/${examId}/assignments`, { assignments }))
}
