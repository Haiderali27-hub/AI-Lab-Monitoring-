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
  // Module 12 hash chain (null for rows written before the chain, until the backfill seals them)
  sequence: number | null;
  hash: string | null;
}

// GET /api/audit-logs/verify — result of walking the tamper-evident hash chain
export interface AuditChainStatus {
  valid: boolean;
  brokenAt: number | null;
  reason: string | null;
  checkedCount: number;
  unsealed: number;
  lastVerifiedSequence: number | null;
  lastVerifiedHash: string | null;
  verifiedAtUtc: string;
}

export const auditApi = {
  // GET /api/audit-logs?eventType=&take=200 — newest first (Admin/SuperAdmin)
  getLogs: (eventType?: string, take = 200) =>
    apiClient
      .get<AuditLogEntry[]>('/audit-logs', {
        params: { eventType: eventType || undefined, take },
      })
      .then(r => r.data),

  // GET /api/audit-logs/verify — { valid, brokenAt, reason, unsealed, … } (Admin/SuperAdmin)
  verifyChain: () => apiClient.get<AuditChainStatus>('/audit-logs/verify').then(r => r.data),

  // POST /api/audit-logs/backfill — seals rows written before the hash chain existed (once, after the migration)
  backfillChain: () =>
    apiClient
      .post<{ sealed: number; valid: boolean; brokenAt: number | null; reason: string | null }>('/audit-logs/backfill')
      .then(r => r.data),
};
