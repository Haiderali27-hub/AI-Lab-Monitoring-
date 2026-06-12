import apiClient from './client';

export const studentApi = {
  // GET /api/student/dashboard
  getDashboard: () =>
    apiClient.get('/student/dashboard').then(r => r.data),

  // GET /api/student/exams
  getMyExams: () =>
    apiClient.get('/student/exams').then(r => r.data),

  // GET /api/student/exams/{examId}/result
  getExamResult: (examId: string) =>
    apiClient.get(`/student/exams/${examId}/result`).then(r => r.data),

  // GET /api/student/violations
  getViolations: () =>
    apiClient.get('/student/violations').then(r => r.data),

  // GET /api/student/performance-trend
  getPerformanceTrend: () =>
    apiClient.get('/student/performance-trend').then(r => r.data),

  // GET /api/student/notifications
  getNotifications: () =>
    apiClient.get('/student/notifications').then(r => r.data),

  // PATCH /api/student/notifications/{id}/read
  markNotificationRead: (id: string) =>
    apiClient.patch(`/student/notifications/${id}/read`),
};
