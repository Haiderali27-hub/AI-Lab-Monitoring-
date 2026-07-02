import apiClient from './client';

export interface AuditLogEntry {
  logId: string;
  eventType: string;
  entityType: string | null;
  entityId: string | null;
  details: string | null;
  createdAt: string;
  actorName: string;
  actorEmail: string | null;
}

export const auditApi = {
  // GET /api/audit-logs?eventType=&take=200 — newest first (Admin/SuperAdmin)
  getLogs: (eventType?: string, take = 200) =>
    apiClient
      .get<AuditLogEntry[]>('/audit-logs', {
        params: { eventType: eventType || undefined, take },
      })
      .then(r => r.data),
};
