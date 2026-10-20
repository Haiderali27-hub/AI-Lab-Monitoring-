import apiClient from './client';
import type {
  Incident, IncidentSeverity, IncidentStatus, LiveSnapshot, ScreenshotSummary, SessionProcesses, SessionTimelineItem,
} from '../types';

/**
 * Module 5 / 10 — live-monitoring evidence and proctoring for staff. Every call is checked server-side against who may
 * supervise the exam (owner teacher, assigned proctor, admin). Images are fetched as blobs with the bearer token (an
 * <img src> cannot carry it) and shown through object URLs, which the caller must revoke.
 */
export const monitoringApi = {
  /** The board as the server knows it: every student who has heartbeated, with their latest state. */
  live: (examId: string) =>
    apiClient.get<LiveSnapshot>(`/exams/${examId}/live`).then(r => r.data),

  screenshots: (examId: string, sessionId: string) =>
    apiClient.get<ScreenshotSummary[]>(`/exams/${examId}/sessions/${sessionId}/screenshots`).then(r => r.data),

  screenshotBlob: (screenshotId: string) =>
    apiClient.get<Blob>(`/screenshots/${screenshotId}/content`, { responseType: 'blob' }).then(r => r.data),

  // `after` is compared with the server's receive time (an event replayed after an outage keeps its old `at`), so
  // the next cursor is the largest `receivedAt ?? at` of the items already shown.
  timeline: (examId: string, sessionId: string, after?: string) =>
    apiClient.get<SessionTimelineItem[]>(`/exams/${examId}/sessions/${sessionId}/timeline`, {
      params: after ? { after } : undefined,
    }).then(r => r.data),

  processes: (examId: string, sessionId: string) =>
    apiClient.get<SessionProcesses>(`/exams/${examId}/sessions/${sessionId}/processes`).then(r => r.data),

  // ── Module 10 incidents ──────────────────────────────────────────────────
  incidents: (examId: string) =>
    apiClient.get<Incident[]>(`/exams/${examId}/incidents`).then(r => r.data),

  createIncident: (sessionId: string, severity: IncidentSeverity, notes: string) =>
    apiClient.post<Incident>('/incidents', { sessionId, severity, notes }).then(r => r.data),

  updateIncident: (incidentId: string, change: { status?: IncidentStatus; notes?: string }) =>
    apiClient.patch<Incident>(`/incidents/${incidentId}`, change).then(r => r.data),
};
