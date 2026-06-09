import apiClient from './client';

export const notificationsApi = {
  // GET /api/notifications
  // Returns logged-in user's notifications + unread count
  getAll: () =>
    apiClient.get('/notifications').then(r => r.data),

  // PATCH /api/notifications/{id}/read
  markRead: (id: string) =>
    apiClient.patch(`/notifications/${id}/read`),

  // PATCH /api/notifications/read-all
  markAllRead: () =>
    apiClient.patch('/notifications/read-all').then(r => r.data),

  // POST /api/notifications/exam/{examId}/announce
  sendAnnouncement: (examId: string, data: {
    title: string;
    message: string;
    sendEmail: boolean;
  }) =>
    apiClient.post(`/notifications/exam/${examId}/announce`, data).then(r => r.data),

  // POST /api/notifications/exam/{examId}/notify-scheduled
  notifyScheduled: (examId: string) =>
    apiClient.post(`/notifications/exam/${examId}/notify-scheduled`).then(r => r.data),

  // POST /api/notifications/exam/{examId}/notify-grades
  notifyGrades: (examId: string) =>
    apiClient.post(`/notifications/exam/${examId}/notify-grades`).then(r => r.data),
};
