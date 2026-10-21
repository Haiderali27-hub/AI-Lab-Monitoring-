import { useCallback, useEffect, useMemo, useState } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { auditApi } from '../../api/audit.api';
import type { AuditChainStatus, AuditLogEntry } from '../../api/audit.api';
import { useToast } from '../../context/ToastContext';

// Module 12: the result of GET /api/audit-logs/verify as one badge (+ the one-off "seal legacy rows" action).
function ChainStatusBadge({ status, checking, error, onRecheck, onBackfill, backfilling }: {
  status: AuditChainStatus | null;
  checking: boolean;
  error: boolean;
  onRecheck: () => void;
  onBackfill: () => void;
  backfilling: boolean;
}) {
  let tone = 'bg-slate-50 text-slate-600 border-slate-200';
  let icon = 'hourglass_empty';
  let label = 'Verifying chain…';
  if (!checking && error) {
    label = 'Chain check failed';
    icon = 'error';
  } else if (!checking && status) {
    if (status.valid) {
      tone = 'bg-green-50 text-green-700 border-green-200';
      icon = 'verified';
      label = `Chain verified ✓ (${status.checkedCount} entries)`;
    } else if (status.brokenAt !== null) {
      tone = 'bg-red-50 text-red-700 border-red-200';
      icon = 'gpp_bad';
      label = `Chain broken at #${status.brokenAt}`;
    } else {
      tone = 'bg-amber-50 text-amber-700 border-amber-200';
      icon = 'pending';
      label = `${status.unsealed} entr${status.unsealed === 1 ? 'y' : 'ies'} not sealed yet`;
    }
  }

  const needsBackfill = !checking && status !== null && !status.valid && status.brokenAt === null && status.unsealed > 0;
  const title = status
    ? [status.reason, status.lastVerifiedHash ? `Last verified #${status.lastVerifiedSequence}: ${status.lastVerifiedHash}` : null]
        .filter(Boolean).join('\n')
    : undefined;

  return (
    <div className="flex items-center gap-2">
      <span
        data-testid="audit-chain-status"
        title={title}
        className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border text-xs font-bold ${tone}`}
      >
        <span className="material-symbols-outlined text-[16px]">{icon}</span>
        {label}
      </span>
      {needsBackfill && (
        <button
          onClick={onBackfill}
          disabled={backfilling}
          className="px-3 py-1.5 rounded-lg border border-amber-300 bg-white text-amber-700 text-xs font-semibold hover:bg-amber-50 disabled:opacity-50"
        >
          Seal legacy entries
        </button>
      )}
      <button
        onClick={onRecheck}
        disabled={checking}
        title="Verify the audit hash chain again"
        className="px-3 py-1.5 rounded-lg border border-slate-200 bg-white text-slate-600 text-xs font-semibold hover:bg-slate-50 disabled:opacity-50"
      >
        Re-verify
      </button>
    </div>
  );
}

// Map an event type to a small colored badge style.
function badgeForEvent(eventType: string): string {
  const green = 'bg-green-50 text-green-700 border-green-200';
  const red = 'bg-red-50 text-red-700 border-red-200';
  const blue = 'bg-blue-50 text-primary-700 border-blue-200';
  const slate = 'bg-slate-50 text-slate-600 border-slate-200';

  const type = eventType.toUpperCase();
  if (type === 'LOGIN_SUCCESS') return green;
  if (type === 'DEVICE_MISMATCH' || type === 'LOGIN_FAILED' || type === 'USER_DELETED') return red;
  if (type.includes('CREATE') || type.includes('LOGIN')) return blue;
  return slate;
}

export default function AuditLogsPage() {
  const [logs, setLogs] = useState<AuditLogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [eventFilter, setEventFilter] = useState<string>('All');
  const { showToast } = useToast();
  const [chain, setChain] = useState<AuditChainStatus | null>(null);
  const [chainChecking, setChainChecking] = useState(true);
  const [chainError, setChainError] = useState(false);
  const [backfilling, setBackfilling] = useState(false);

  const verifyChain = useCallback(() => {
    setChainChecking(true);
    setChainError(false);
    auditApi
      .verifyChain()
      .then(setChain)
      .catch(err => {
        console.error('Failed to verify the audit chain', err);
        setChainError(true);
      })
      .finally(() => setChainChecking(false));
  }, []);

  useEffect(() => verifyChain(), [verifyChain]);

  const backfillChain = () => {
    setBackfilling(true);
    auditApi
      .backfillChain()
      .then(r => {
        showToast('Audit chain', `Sealed ${r.sealed} legacy entr${r.sealed === 1 ? 'y' : 'ies'}.`, r.valid ? 'success' : 'error');
        verifyChain();
      })
      .catch(() => showToast('Audit chain', 'Sealing the legacy entries failed.', 'error'))
      .finally(() => setBackfilling(false));
  };

  useEffect(() => {
    let active = true;
    setLoading(true);
    auditApi
      .getLogs()
      .then(data => {
        if (active) setLogs(data);
      })
      .catch(err => {
        console.error('Failed to load audit logs', err);
        if (active) setError('Failed to load audit logs. Please ensure the backend is active.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  // Distinct event types derived from the loaded data
  const eventTypes = useMemo(() => {
    const set = new Set(logs.map(l => l.eventType));
    return ['All', ...Array.from(set).sort()];
  }, [logs]);

  const filteredLogs = useMemo(() => {
    const q = searchQuery.toLowerCase();
    return logs.filter(log => {
      const matchesSearch =
        !q ||
        log.actorName.toLowerCase().includes(q) ||
        (log.actorEmail?.toLowerCase().includes(q) ?? false) ||
        log.eventType.toLowerCase().includes(q) ||
        (log.entityType?.toLowerCase().includes(q) ?? false) ||
        (log.details?.toLowerCase().includes(q) ?? false);

      const matchesEvent = eventFilter === 'All' || log.eventType === eventFilter;
      return matchesSearch && matchesEvent;
    });
  }, [logs, searchQuery, eventFilter]);

  return (
    <AppLayout title="Audit Logs">
      <div className="max-w-[1280px] mx-auto pb-12">

        {/* Sub-header */}
        <div className="mb-8 flex flex-col md:flex-row md:items-end justify-between gap-4">
          <div>
            <h3 className="text-xl font-bold text-slate-800">Security & Administrative Logs</h3>
            <p className="text-sm text-slate-400 mt-0.5">Chronological record of system modifications, resets, and exam management actions. Every entry is hash-chained to the one before it, so a changed or deleted entry is detected.</p>
          </div>
          <ChainStatusBadge
            status={chain}
            checking={chainChecking}
            error={chainError}
            onRecheck={verifyChain}
            onBackfill={backfillChain}
            backfilling={backfilling}
          />
        </div>

        {/* Searching & Filters Controls */}
        <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm mb-6 flex flex-col md:flex-row gap-4 items-center justify-between">
          <div className="relative w-full md:w-[320px] flex items-center">
            <input
              type="text"
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              placeholder="Search logs..."
              className="w-full pl-10 pr-4 py-2 border border-slate-200 rounded-xl text-xs focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800"
            />
            <span className="material-symbols-outlined absolute left-3 text-slate-400 text-[20px] pointer-events-none">search</span>
          </div>

          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-slate-400 text-[20px]">filter_list</span>
            <select
              value={eventFilter}
              onChange={e => setEventFilter(e.target.value)}
              className="bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-xs font-semibold text-slate-700 focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              {eventTypes.map(type => (
                <option key={type} value={type}>{type}</option>
              ))}
            </select>
          </div>
        </div>

        {/* Logs Table Container */}
        <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
          {loading ? (
            <div className="min-h-[240px] flex items-center justify-center">
              <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
            </div>
          ) : error ? (
            <div className="text-center py-12 text-red-500 text-sm">{error}</div>
          ) : logs.length === 0 ? (
            <div className="text-center py-12 text-slate-400 italic text-sm">
              No audit events recorded yet.
            </div>
          ) : filteredLogs.length === 0 ? (
            <div className="text-center py-12 text-slate-400 italic text-sm">
              No audit logs match the current search or filters.
            </div>
          ) : (
            <table className="w-full text-left border-collapse">
              <thead className="bg-slate-50 border-b border-slate-200 text-xs font-bold text-slate-500 uppercase tracking-wider">
                <tr>
                  <th className="px-6 py-4">Timestamp</th>
                  <th className="px-6 py-4">Actor</th>
                  <th className="px-6 py-4">Event</th>
                  <th className="px-6 py-4">Entity</th>
                  <th className="px-6 py-4">Details</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 text-xs text-slate-700">
                {filteredLogs.map(log => (
                  <tr key={log.logId} className="hover:bg-slate-50/30 transition-colors">
                    <td className="px-6 py-4 font-mono text-slate-400 whitespace-nowrap">
                      {new Date(log.createdAt).toLocaleString()}
                    </td>
                    <td className="px-6 py-4">
                      <div className="font-semibold text-slate-800">{log.actorName}</div>
                      {log.actorEmail && (
                        <div className="text-[10px] text-slate-400 font-mono">{log.actorEmail}</div>
                      )}
                    </td>
                    <td className="px-6 py-4">
                      <span className={`px-2 py-0.5 rounded border text-[9px] font-bold uppercase tracking-wider ${badgeForEvent(log.eventType)}`}>
                        {log.eventType}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {log.entityType ? (
                        <span className="text-slate-600 font-semibold">{log.entityType}</span>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )}
                    </td>
                    <td className="px-6 py-4 max-w-md">
                      {log.details ? (
                        <span className="inline-block max-w-full truncate align-bottom font-mono text-[10px] bg-slate-50 border border-slate-200 text-slate-600 px-2 py-1 rounded" title={log.details}>
                          {log.details}
                        </span>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

      </div>
    </AppLayout>
  );
}
