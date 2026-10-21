import apiClient from './client';

export type ReportExportType = 'attendance' | 'violations' | 'scores' | 'excel' | 'evidence-zip';

export const analyticsApi = {
  // GET /api/analytics/system
  // Admin only — system-wide stats and charts
  getSystemAnalytics: () =>
    apiClient.get('/analytics/system').then(r => r.data),

  // GET /api/analytics/exams/{examId}/summary
  // Teacher/Admin — single exam breakdown
  getExamSummary: (examId: string) =>
    apiClient.get(`/analytics/exams/${examId}/summary`).then(r => r.data),

  // GET /api/analytics/exams/{examId}/reports
  // List previously generated PDF reports for an exam
  getReportHistory: (examId: string) =>
    apiClient.get(`/analytics/exams/${examId}/reports`).then(r => r.data),

  // POST /api/analytics/exams/{examId}/report
  // Generate a new PDF report — returns downloadUrl
  generateReport: (examId: string) =>
    apiClient.post(`/analytics/exams/${examId}/report`).then(r => r.data),

  // POST /api/analytics/reports/{type}  body { examId } — Module 12 exports
  // attendance | violations | scores (PDF), excel (.xlsx), evidence-zip (.zip with SHA-256 manifest)
  generateExport: (type: ReportExportType, examId: string) =>
    apiClient
      .post<{ downloadUrl: string; fileName: string; reportType: string; sha256: string }>(`/analytics/reports/${type}`, { examId })
      .then(r => r.data),

  // GET /api/analytics/reports/{fileName}
  // Download a generated report file (PDF, Excel or ZIP) — fetched with the token, saved as a blob
  getReportDownloadUrl: (fileName: string) =>
    `${apiClient.defaults.baseURL}/analytics/reports/${fileName}`,
};
