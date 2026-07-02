import { useEffect, useMemo, useState } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { auditApi } from '../../api/audit.api';
import type { AuditLogEntry } from '../../api/audit.api';

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
        <div className="mb-8">
          <h3 className="text-xl font-bold text-slate-800">Security & Administrative Logs</h3>
          <p className="text-sm text-slate-400 mt-0.5">Chronological record of system modifications, resets, and exam management actions.</p>
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
