import { useState } from 'react';
import AppLayout from '../../components/layout/AppLayout';

interface AuditLog {
  id: string;
  timestamp: string;
  actor: string;
  role: string;
  action: string;
  details: string;
  ipAddress: string;
  severity: 'Success' | 'Warning' | 'Danger' | 'Info';
}

export default function AuditLogsPage() {
  const [logs] = useState<AuditLog[]>([
    { id: '1', timestamp: '2026-06-02 18:10:45', actor: 'admin@smartexam.com', role: 'Admin', action: 'Device Binding Reset', details: 'Reset hardware signature binding for Student: Alex Sterling (alex@sterling.com)', ipAddress: '192.168.1.10', severity: 'Warning' },
    { id: '2', timestamp: '2026-06-02 17:55:12', actor: 'Dr. Ahmed', role: 'Teacher', action: 'Create Exam', details: 'Created exam "CS301 - Mid-Term Lab Exam" for Section A', ipAddress: '192.168.10.150', severity: 'Success' },
    { id: '3', timestamp: '2026-06-02 17:42:01', actor: 'admin@smartexam.com', role: 'Admin', action: 'User Session Revoked', details: 'Force-logged out student session for Student: Michael Chen (michael@chen.com)', ipAddress: '192.168.1.10', severity: 'Danger' },
    { id: '4', timestamp: '2026-06-02 17:30:00', actor: 'System Daemon', role: 'System', action: 'Auto-Grading Complete', details: 'Finished AI Grading for exam CS102 (Data Structures)', ipAddress: '127.0.0.1', severity: 'Info' },
    { id: '5', timestamp: '2026-06-02 16:15:33', actor: 'admin@smartexam.com', role: 'Admin', action: 'Create User', details: 'Created user account for Teacher: Dr. Ahmed (ahmed@university.edu)', ipAddress: '192.168.1.10', severity: 'Success' },
    { id: '6', timestamp: '2026-06-02 15:44:20', actor: 'Dr. Ahmed', role: 'Teacher', action: 'Update Eligibility', details: 'Marked student Jordan Smith ineligible (Reason: Low Attendance)', ipAddress: '192.168.10.150', severity: 'Warning' },
    { id: '7', timestamp: '2026-06-02 15:20:10', actor: 'System Daemon', role: 'System', action: 'Database Seed', details: 'Neon serverless Postgres seeded successfully', ipAddress: '127.0.0.1', severity: 'Info' }
  ]);

  const [searchQuery, setSearchQuery] = useState('');
  const [severityFilter, setSeverityFilter] = useState<'All' | 'Success' | 'Warning' | 'Danger' | 'Info'>('All');

  const filteredLogs = logs.filter(log => {
    const matchesSearch = log.actor.toLowerCase().includes(searchQuery.toLowerCase()) || 
                          log.action.toLowerCase().includes(searchQuery.toLowerCase()) ||
                          log.details.toLowerCase().includes(searchQuery.toLowerCase());
    
    const matchesSeverity = severityFilter === 'All' || log.severity === severityFilter;
    
    return matchesSearch && matchesSeverity;
  });

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

          <div className="flex gap-2">
            {['All', 'Success', 'Warning', 'Danger', 'Info'].map(level => (
              <button
                key={level}
                onClick={() => setFilter(level as any)}
                className={`px-3 py-1.5 text-xs font-semibold rounded-lg transition-colors ${
                  severityFilter === level
                    ? 'bg-slate-100 text-slate-800'
                    : 'text-slate-500 hover:text-slate-800'
                }`}
              >
                {level}
              </button>
            ))}
          </div>
        </div>

        {/* Logs Table Container */}
        <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
          {filteredLogs.length === 0 ? (
            <div className="text-center py-12 text-slate-400 italic text-sm">
              No audit logs match the current search or filters.
            </div>
          ) : (
            <table className="w-full text-left border-collapse">
              <thead className="bg-slate-50 border-b border-slate-200 text-xs font-bold text-slate-500 uppercase tracking-wider">
                <tr>
                  <th className="px-6 py-4">Timestamp</th>
                  <th className="px-6 py-4">Actor</th>
                  <th className="px-6 py-4">Action</th>
                  <th className="px-6 py-4">Details</th>
                  <th className="px-6 py-4">IP Address</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 text-xs text-slate-700">
                {filteredLogs.map(log => {
                  let badgeColor = 'bg-slate-50 text-slate-600 border-slate-200';
                  if (log.severity === 'Success') badgeColor = 'bg-green-50 text-green-700 border-green-200';
                  if (log.severity === 'Warning') badgeColor = 'bg-yellow-50 text-yellow-700 border-yellow-200';
                  if (log.severity === 'Danger') badgeColor = 'bg-red-50 text-red-700 border-red-200';
                  if (log.severity === 'Info') badgeColor = 'bg-blue-50 text-primary-700 border-blue-200';

                  return (
                    <tr key={log.id} className="hover:bg-slate-50/30 transition-colors">
                      <td className="px-6 py-4 font-mono text-slate-400 whitespace-nowrap">{log.timestamp}</td>
                      <td className="px-6 py-4">
                        <div className="font-semibold text-slate-800">{log.actor}</div>
                        <div className="text-[10px] text-slate-400 uppercase font-semibold">{log.role}</div>
                      </td>
                      <td className="px-6 py-4">
                        <span className={`px-2 py-0.5 rounded border text-[9px] font-bold uppercase tracking-wider ${badgeColor}`}>
                          {log.action}
                        </span>
                      </td>
                      <td className="px-6 py-4 leading-relaxed text-slate-600 max-w-md">{log.details}</td>
                      <td className="px-6 py-4 font-mono text-slate-400">{log.ipAddress}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>

      </div>
    </AppLayout>
  );

  // Helper alias inside component scope to avoid conflicts
  function setFilter(level: 'All' | 'Success' | 'Warning' | 'Danger' | 'Info') {
    setSeverityFilter(level);
  }
}
