import apiClient from './client';

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

  // GET /api/analytics/reports/{fileName}
  // Download a PDF — open in new tab
  getReportDownloadUrl: (fileName: string) =>
    `${apiClient.defaults.baseURL}/analytics/reports/${fileName}`,
};
