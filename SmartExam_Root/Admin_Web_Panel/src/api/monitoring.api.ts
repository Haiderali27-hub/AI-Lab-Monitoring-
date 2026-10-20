import apiClient from './client';
import type { ScreenshotSummary, SessionTimelineItem } from '../types';

/**
 * Module 5 — live-monitoring evidence for staff. Every call is checked server-side against who may supervise the
 * exam (owner teacher, assigned proctor, admin). Images are fetched as blobs with the bearer token (an <img src>
 * cannot carry it) and shown through object URLs, which the caller must revoke.
 */
export const monitoringApi = {
  screenshots: (examId: string, sessionId: string) =>
    apiClient.get<ScreenshotSummary[]>(`/exams/${examId}/sessions/${sessionId}/screenshots`).then(r => r.data),

  screenshotBlob: (screenshotId: string) =>
    apiClient.get<Blob>(`/screenshots/${screenshotId}/content`, { responseType: 'blob' }).then(r => r.data),

  timeline: (examId: string, sessionId: string, after?: string) =>
    apiClient.get<SessionTimelineItem[]>(`/exams/${examId}/sessions/${sessionId}/timeline`, {
      params: after ? { after } : undefined,
    }).then(r => r.data),
};
