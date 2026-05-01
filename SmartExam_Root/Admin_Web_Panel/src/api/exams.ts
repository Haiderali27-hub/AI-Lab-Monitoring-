import type { CreateExamPayload, ExamCandidate, ExamSummary, LiveRosterItem, Proctor } from '../types'
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

export async function updateExamAssignments(examId: string, assignments: CreateExamPayload['assignments']): Promise<void> {
  await unwrap<{ examId: string }>(apiClient.put(`/api/exams/${examId}/assignments`, { assignments }))
}