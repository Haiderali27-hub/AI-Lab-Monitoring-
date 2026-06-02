import type { ExamReportDetail, ExamReportSummary } from '../types'
import { apiClient, unwrap } from './client'

export async function getExamReports(): Promise<ExamReportSummary[]> {
  return unwrap<ExamReportSummary[]>(apiClient.get('/api/reports/exams'))
}

export async function getExamReportDetail(examId: string): Promise<ExamReportDetail> {
  return unwrap<ExamReportDetail>(apiClient.get(`/api/reports/exams/${examId}`))
}
