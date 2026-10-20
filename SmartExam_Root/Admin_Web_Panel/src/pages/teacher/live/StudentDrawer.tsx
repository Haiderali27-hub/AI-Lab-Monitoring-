import { useEffect, useState } from 'react';
import { monitoringApi } from '../../../api/monitoring.api';
import { useToast } from '../../../context/ToastContext';
import type {
  Exam, Incident, IncidentSeverity, IncidentStatus, ReadinessResult, SessionProcesses, SessionTimelineItem, StudentLiveStatus,
} from '../../../types';
import ScreenshotCarousel from './ScreenshotCarousel';
import { describeTimelineItem, READINESS_LABEL, readinessState } from './timeline';

// The server timeline is re-read shortly after a live event for this student (several often arrive together) and
// every 15 s while the drawer is open (events that have no live push, e.g. app changes).
const REFRESH_DEBOUNCE_MS = 400;
const POLL_MS = 15_000;
const UNLOCK_MINUTES = 5;
const DEFAULT_UNLOCK_REASON = 'Invigilator assisting at the workstation';
const SEVERITIES: IncidentSeverity[] = ['Low', 'Medium', 'High', 'Critical'];

const SEVERITY_STYLE: Record<IncidentSeverity, string> = {
  Low: 'bg-slate-100 text-slate-600',
  Medium: 'bg-amber-100 text-amber-700',
  High: 'bg-orange-100 text-orange-700',
  Critical: 'bg-red-100 text-red-700',
};

function apiError(err: unknown, fallback: string): string {
  const e = err as { response?: { data?: { message?: string } }; message?: string } | null;
  return e?.response?.data?.message || e?.message?.replace(/^.*HubException: /, '') || fallback;
}

function isFinalised(status?: string | null) {
  return !!status && status !== 'InProgress';
}

function sessionStatusLabel(status?: string | null): string {
  switch (status) {
    case 'Submitted': return 'Submitted';
    case 'ForceSubmitted': return 'Force submitted';
    case 'TimedOut': return 'Timed out';
    default: return 'In progress';
  }
}

interface App {
  name: string;
  running: boolean;
  lastSeenAt?: string;
}

// Apps: running now (the latest heartbeat, live; else the server's latest) first, then the ones seen earlier.
function appsOf(liveSnapshot: string | null | undefined, server: SessionProcesses | null): App[] {
  const live = (liveSnapshot ?? '').split(',').map(s => s.trim()).filter(s => s && s !== 'UNAVAILABLE');
  const current = live.length ? live : server?.current ?? [];
  const names = new Map<string, App>();
  for (const n of current) names.set(n.toLowerCase(), { name: n, running: true });
  for (const s of server?.seen ?? []) {
    if (!names.has(s.name.toLowerCase())) names.set(s.name.toLowerCase(), { name: s.name, running: false, lastSeenAt: s.lastSeenAt });
  }
  return Array.from(names.values()).sort((a, b) => Number(b.running) - Number(a.running) || a.name.localeCompare(b.name));
}

export interface StudentDrawerProps {
  exam: Exam;
  student: StudentLiveStatus;
  readiness?: ReadinessResult;
  incidents: Incident[];
  /** Changes whenever a live event about this student arrives (re-reads timeline, captures and apps). */
  refreshKey: number;
  isInvigilator: boolean;
  onClose: () => void;
  onSendWarning: (message: string) => Promise<void>;
  onForceSubmit: () => void;
  onExtendTime: () => void;
  onUnlock: (minutes: number, reason: string) => Promise<void>;
  onRelock: () => Promise<void>;
  onIncidentSaved: (incident: Incident) => void;
}

/**
 * Module 10 — everything about one student in the Live Monitor: latest captures, readiness result, the apps the PC
 * reported, incidents, and the server-side activity timeline (violations, captures, warnings, proctor actions,
 * status changes; it survives a page reload). Actions: warn, mark an incident, unlock the lockdown for a few
 * minutes, extra time, force-submit.
 */
export default function StudentDrawer(props: StudentDrawerProps) {
  const { exam, student, readiness, incidents, refreshKey, onClose } = props;
  const { showToast } = useToast();
  const sessionId = student.sessionId;

  const [timeline, setTimeline] = useState<SessionTimelineItem[]>([]);
  const [timelineLoaded, setTimelineLoaded] = useState(false);
  const [processes, setProcesses] = useState<SessionProcesses | null>(null);
  const [message, setMessage] = useState('');
  const [warningSent, setWarningSent] = useState(false);
  const [incidentFormOpen, setIncidentFormOpen] = useState(false);
  const [incidentNotes, setIncidentNotes] = useState('');
  const [incidentSeverity, setIncidentSeverity] = useState<IncidentSeverity>('Medium');
  const [saving, setSaving] = useState(false);
  const [unlockReason, setUnlockReason] = useState(DEFAULT_UNLOCK_REASON);
  const [pendingChange, setPendingChange] = useState<{ incidentId: string; status: IncidentStatus } | null>(null);
  const [changeNote, setChangeNote] = useState('');

  // Ticks so an unlock badge disappears when it runs out (the page re-mounts the drawer per session: `key`).
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 5_000);
    return () => clearInterval(t);
  }, []);

  // Server timeline and apps: now, shortly after each live event, and on a slow poll.
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    const load = () => {
      monitoringApi.timeline(exam.examId, sessionId)
        .then(items => { if (!cancelled) { setTimeline(items); setTimelineLoaded(true); } })
        .catch(err => console.error('Failed to load the session timeline', err));
      monitoringApi.processes(exam.examId, sessionId)
        .then(p => { if (!cancelled) setProcesses(p); })
        .catch(err => console.error('Failed to load the session apps', err));
    };
    const first = setTimeout(load, refreshKey === 0 ? 0 : REFRESH_DEBOUNCE_MS);
    const poll = setInterval(load, POLL_MS);
    return () => {
      cancelled = true;
      clearTimeout(first);
      clearInterval(poll);
    };
  }, [exam.examId, sessionId, refreshKey]);

  useEffect(() => {
    if (!warningSent) return;
    const t = setTimeout(() => setWarningSent(false), 8_000);
    return () => clearTimeout(t);
  }, [warningSent]);

  const entries = timeline.map(describeTimelineItem);
  const apps = appsOf(student.processes, processes);

  const finalised = isFinalised(student.sessionStatus);
  const unlockedUntil = student.unlockedUntil && new Date(student.unlockedUntil).getTime() > now ? student.unlockedUntil : null;
  const rState = readinessState(readiness);
  const openIncidents = incidents.filter(i => i.status !== 'Resolved').length;

  const sendWarning = async () => {
    const text = message.trim();
    if (!text) return;
    try {
      await props.onSendWarning(text);
      setMessage('');
      setWarningSent(true);
    } catch (err) {
      showToast('Warning not sent', apiError(err, 'The live connection is down. Try again in a moment.'), 'error');
    }
  };

  const saveIncident = async () => {
    const notes = incidentNotes.trim();
    if (!notes || saving) return;
    setSaving(true);
    try {
      const incident = await monitoringApi.createIncident(sessionId, incidentSeverity, notes);
      props.onIncidentSaved(incident);
      setIncidentNotes('');
      setIncidentSeverity('Medium');
      setIncidentFormOpen(false);
      showToast('Incident recorded', `${incident.severity} incident for ${student.studentName}.`, 'success');
    } catch (err) {
      showToast('Incident not saved', apiError(err, 'Could not save the incident.'), 'error');
    } finally {
      setSaving(false);
    }
  };

  const changeIncident = async () => {
    if (!pendingChange || saving) return;
    setSaving(true);
    try {
      const updated = await monitoringApi.updateIncident(pendingChange.incidentId, {
        status: pendingChange.status,
        notes: changeNote.trim() || undefined,
      });
      props.onIncidentSaved(updated);
      setPendingChange(null);
      setChangeNote('');
    } catch (err) {
      showToast('Incident not updated', apiError(err, 'Could not update the incident.'), 'error');
    } finally {
      setSaving(false);
    }
  };

  const unlock = async () => {
    const reason = unlockReason.trim();
    if (!reason || saving) return;
    setSaving(true);
    try {
      await props.onUnlock(UNLOCK_MINUTES, reason);
      showToast('PC unlocked', `Unlocked for ${UNLOCK_MINUTES} minutes. Lockdown resumes on its own; monitoring continues.`, 'success');
    } catch (err) {
      showToast('Unlock failed', apiError(err, 'Could not unlock the PC.'), 'error');
    } finally {
      setSaving(false);
    }
  };

  const relock = async () => {
    setSaving(true);
    try {
      await props.onRelock();
      showToast('Lockdown restored', `${student.studentName}'s PC is locked again.`, 'info');
    } catch (err) {
      showToast('Lock failed', apiError(err, 'Could not lock the PC again.'), 'error');
    } finally {
      setSaving(false);
    }
  };

  return (
    <aside data-testid="student-drawer" className="fixed right-0 top-[56px] bottom-0 w-[360px] bg-white border-l border-slate-200 shadow-2xl flex flex-col z-50 animate-in slide-in-from-right duration-200">
      {/* Header */}
      <div className="p-5 border-b border-slate-100 shrink-0">
        <div className="flex justify-between items-start mb-3">
          <div className="w-11 h-11 rounded-2xl bg-blue-100 text-primary-600 font-bold flex items-center justify-center text-base">
            {student.studentName.split(' ').map((n: string) => n[0]).join('')}
          </div>
          <button onClick={onClose} aria-label="Close" className="p-1 hover:bg-slate-100 rounded-lg transition-colors text-slate-400">
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>
        <h3 className="font-bold text-slate-800 text-lg leading-tight">{student.studentName}</h3>
        <p className="text-xs text-slate-400">
          Seat {student.workstationNumber}
          {student.machineName ? ` · ${student.machineName}` : ''}
          {student.windowsUsername ? ` · Windows user: ${student.windowsUsername}` : ''}
        </p>
        <div className="flex flex-wrap items-center gap-1.5 mt-3">
          {student.violationCount > 0 ? (
            <span className="px-2 py-0.5 bg-red-100 text-red-700 rounded text-[9px] font-bold uppercase">{student.violationCount} Alert(s) Active</span>
          ) : (
            <span className="px-2 py-0.5 bg-green-100 text-green-700 rounded text-[9px] font-bold uppercase">Stable Connection</span>
          )}
          {openIncidents > 0 && (
            <span className="px-2 py-0.5 bg-orange-100 text-orange-700 rounded text-[9px] font-bold uppercase">{openIncidents} open incident(s)</span>
          )}
          {unlockedUntil && (
            <span data-testid="drawer-unlocked" className="px-2 py-0.5 bg-blue-100 text-blue-700 rounded text-[9px] font-bold uppercase">
              Unlocked until {new Date(unlockedUntil).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
            </span>
          )}
        </div>
      </div>

      {/* Body */}
      <div className="flex-grow overflow-y-auto p-5 space-y-6 scrollbar-thin">
        <section>
          <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-2">Latest Captures</h4>
          <ScreenshotCarousel key={sessionId} examId={exam.examId} sessionId={sessionId} refreshKey={refreshKey} />
        </section>

        <section data-testid="drawer-readiness">
          <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-2">Readiness Check</h4>
          {readiness ? (
            <div className={`rounded-lg border p-2.5 text-xs ${rState === 'passed' ? 'border-green-200 bg-green-50/50' : 'border-red-200 bg-red-50/50'}`}>
              <p className={`font-semibold ${rState === 'passed' ? 'text-green-700' : 'text-red-700'}`}>
                {rState === 'passed' ? 'All checks passed' : 'Some checks failed'} · {new Date(readiness.checkedAt).toLocaleTimeString()}
                {readiness.machineName ? ` · ${readiness.machineName}` : ''}
              </p>
              <ul className="mt-1.5 space-y-0.5">
                {readiness.results.map(r => (
                  <li key={r.name} className="flex items-start gap-1.5 text-[11px] text-slate-600">
                    <span className={`material-symbols-outlined text-[14px] ${r.passed ? 'text-green-500' : 'text-red-500'}`}>{r.passed ? 'check_circle' : 'cancel'}</span>
                    <span><span className="font-semibold">{r.name}</span>{r.detail ? ` — ${r.detail}` : ''}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="text-[11px] text-slate-400 italic">{READINESS_LABEL.none}: the student has not run the pre-exam system check.</p>
          )}
        </section>

        <section>
          <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-2">Apps on this PC</h4>
          <div data-testid="process-list" className="flex flex-wrap gap-1">
            {apps.length === 0 && <span className="text-[11px] text-slate-400 italic">No app list reported yet.</span>}
            {apps.map(a => (
              <span
                key={a.name}
                title={a.running ? 'Running now' : `Seen earlier${a.lastSeenAt ? `, last at ${new Date(a.lastSeenAt).toLocaleTimeString()}` : ''}; not running now`}
                className={`px-1.5 py-0.5 rounded text-[10px] font-semibold ${a.running ? 'bg-slate-100 text-slate-700' : 'bg-white border border-dashed border-slate-300 text-slate-400 line-through'}`}
              >
                {a.name}
              </span>
            ))}
          </div>
        </section>

        <section>
          <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-2">Incidents</h4>
          {incidents.length === 0 ? (
            <p className="text-[11px] text-slate-400 italic">None recorded.</p>
          ) : (
            <ul data-testid="incident-list" className="space-y-2">
              {incidents.map(i => (
                <li key={i.incidentId} className="rounded-lg border border-slate-200 p-2.5 text-xs">
                  <div className="flex items-center gap-1.5">
                    <span className={`px-1.5 py-0.5 rounded text-[9px] font-bold uppercase ${SEVERITY_STYLE[i.severity]}`}>{i.severity}</span>
                    <span className="text-[10px] font-bold uppercase text-slate-500">{i.status}</span>
                    <span className="ml-auto text-[10px] text-slate-400">{new Date(i.createdAt).toLocaleTimeString()}</span>
                  </div>
                  <p className="mt-1 text-slate-700">{i.notes}</p>
                  <p className="text-[10px] text-slate-400 mt-0.5">
                    By {i.createdByName ?? 'a proctor'}{i.statusNote ? ` · ${i.status === 'Resolved' ? 'Resolution' : 'Note'}: ${i.statusNote}` : ''}
                  </p>
                  {i.status !== 'Resolved' && (pendingChange?.incidentId === i.incidentId ? (
                    <div className="mt-2 flex items-center gap-1">
                      <input
                        aria-label={pendingChange.status === 'Resolved' ? 'Resolution note' : 'Escalation note'}
                        value={changeNote}
                        onChange={e => setChangeNote(e.target.value)}
                        maxLength={2000}
                        placeholder={pendingChange.status === 'Resolved' ? 'How was it resolved? (optional)' : 'Why escalate? (optional)'}
                        className="flex-grow min-w-0 px-2 py-1 border border-slate-200 rounded-lg text-[11px] outline-none focus:border-primary-500"
                      />
                      <button onClick={changeIncident} disabled={saving} className="px-2 py-1 bg-primary-500 text-white rounded-lg text-[10px] font-bold disabled:opacity-50">
                        {pendingChange.status === 'Resolved' ? 'Resolve' : 'Escalate'}
                      </button>
                      <button onClick={() => setPendingChange(null)} className="px-2 py-1 text-slate-500 text-[10px] font-bold">Cancel</button>
                    </div>
                  ) : (
                    <div className="mt-2 flex gap-1">
                      {i.status === 'Open' && (
                        <button onClick={() => { setPendingChange({ incidentId: i.incidentId, status: 'Escalated' }); setChangeNote(''); }}
                          className="px-2 py-1 border border-orange-200 text-orange-700 rounded-lg text-[10px] font-bold hover:bg-orange-50">
                          Escalate
                        </button>
                      )}
                      <button onClick={() => { setPendingChange({ incidentId: i.incidentId, status: 'Resolved' }); setChangeNote(''); }}
                        className="px-2 py-1 border border-green-200 text-green-700 rounded-lg text-[10px] font-bold hover:bg-green-50">
                        Mark resolved
                      </button>
                    </div>
                  ))}
                </li>
              ))}
            </ul>
          )}
        </section>

        <section>
          <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-4">Activity Timeline</h4>
          <div data-testid="activity-timeline">
            {entries.length === 0 ? (
              <div className="text-center text-xs text-slate-400 italic py-8">
                {timelineLoaded ? 'No alerts or proctor actions recorded yet. Session is running normally.' : 'Loading activity…'}
              </div>
            ) : (
              <div className="relative border-l-2 border-slate-200 ml-2 space-y-5">
                {entries.map(item => (
                  <div key={item.key} className="relative pl-6">
                    <span className={`absolute -left-[7px] top-1.5 w-3.5 h-3.5 rounded-full border-4 border-white shadow-sm ${item.tone === 'alert' ? 'bg-orange-400' : item.tone === 'action' ? 'bg-primary-500' : 'bg-green-500'}`}></span>
                    <div className="flex flex-col">
                      <span className="text-xs font-semibold text-slate-800">{item.title}</span>
                      <span className="text-[10px] text-slate-400">{item.time}</span>
                      {item.description && (
                        <div className={`mt-1.5 text-[11px] p-2 rounded border break-words ${item.tone === 'alert' ? 'bg-orange-50/50 border-orange-100 text-orange-800' : 'bg-slate-50 border-slate-100 text-slate-600'}`}>
                          {item.description}
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </section>
      </div>

      {/* Actions */}
      <div className="p-5 bg-slate-50 border-t border-slate-200 shrink-0 max-h-[45%] overflow-y-auto">
        <div className="relative mb-1 flex items-center gap-1.5">
          <input
            type="text"
            data-testid="warning-message-input"
            value={message}
            maxLength={500}
            onChange={e => { setMessage(e.target.value); setWarningSent(false); }}
            className="w-full px-3 py-2 border border-slate-200 rounded-xl text-xs focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800 bg-white"
            placeholder="Send warning message..."
            onKeyDown={e => { if (e.key === 'Enter') sendWarning(); }}
          />
          <button
            data-testid="send-warning-button"
            onClick={sendWarning}
            aria-label="Send warning"
            className="p-2 bg-primary-500 text-white rounded-lg hover:bg-primary-600 transition-colors flex items-center justify-center shrink-0"
          >
            <span className="material-symbols-outlined text-[16px]">send</span>
          </button>
        </div>
        <p className="h-4 mb-2 text-[10px] font-semibold text-green-600" role="status">
          {warningSent ? 'Warning Message Sent' : ''}
        </p>

        {finalised ? (
          <p data-testid="drawer-finalised" className="text-center text-xs font-semibold text-slate-500 py-2">
            {sessionStatusLabel(student.sessionStatus)} — the session is locked.
          </p>
        ) : (
          <>
            {incidentFormOpen ? (
              <div data-testid="incident-form" className="mb-2 p-2.5 bg-white border border-slate-200 rounded-xl space-y-2">
                <label htmlFor="incident-notes" className="block text-[10px] font-bold text-slate-500 uppercase tracking-wider">Description</label>
                <textarea
                  id="incident-notes"
                  value={incidentNotes}
                  onChange={e => setIncidentNotes(e.target.value)}
                  maxLength={2000}
                  rows={2}
                  className="w-full px-2 py-1.5 border border-slate-200 rounded-lg text-xs outline-none focus:border-primary-500 resize-none"
                  placeholder="What did you observe?"
                />
                <div className="flex items-center gap-2">
                  <label htmlFor="incident-severity" className="text-[10px] font-bold text-slate-500 uppercase tracking-wider">Severity</label>
                  <select
                    id="incident-severity"
                    value={incidentSeverity}
                    onChange={e => setIncidentSeverity(e.target.value as IncidentSeverity)}
                    className="flex-grow h-7 border border-slate-200 rounded-lg px-1 text-xs bg-white"
                  >
                    {SEVERITIES.map(s => <option key={s} value={s}>{s}</option>)}
                  </select>
                </div>
                <div className="flex gap-1.5">
                  <button onClick={saveIncident} disabled={saving || !incidentNotes.trim()}
                    className="flex-grow py-1.5 bg-orange-500 hover:bg-orange-600 text-white text-xs font-bold rounded-lg disabled:opacity-50">
                    Save incident
                  </button>
                  <button onClick={() => setIncidentFormOpen(false)} className="px-3 py-1.5 text-slate-500 text-xs font-bold">Cancel</button>
                </div>
              </div>
            ) : (
              <button
                onClick={() => setIncidentFormOpen(true)}
                className="w-full mb-2 py-2 bg-white border border-orange-200 hover:bg-orange-50 text-orange-700 text-xs font-bold rounded-xl transition-all flex items-center justify-center gap-1.5"
              >
                <span className="material-symbols-outlined text-[16px]">flag</span>
                Mark incident
              </button>
            )}

            {unlockedUntil ? (
              <button
                onClick={relock}
                disabled={saving}
                className="w-full mb-2 py-2 bg-white border border-blue-200 hover:bg-blue-50 text-blue-700 text-xs font-bold rounded-xl transition-all flex items-center justify-center gap-1.5 disabled:opacity-50"
              >
                <span className="material-symbols-outlined text-[16px]">lock</span>
                Lock again now
              </button>
            ) : (
              <div className="mb-2 flex items-center gap-1.5">
                <input
                  data-testid="unlock-reason-input"
                  aria-label="Unlock reason"
                  value={unlockReason}
                  maxLength={500}
                  onChange={e => setUnlockReason(e.target.value)}
                  title="Recorded in the audit log"
                  className="flex-grow min-w-0 px-2 py-2 border border-slate-200 rounded-xl text-[11px] outline-none focus:border-primary-500 bg-white"
                />
                <button
                  onClick={unlock}
                  disabled={saving || !unlockReason.trim()}
                  title="Suspend the lockdown on this PC (keyboard shortcuts, Task Manager, app blocking) so an invigilator can work on it"
                  className="shrink-0 px-3 py-2 bg-white border border-blue-200 hover:bg-blue-50 text-blue-700 text-xs font-bold rounded-xl flex items-center gap-1 disabled:opacity-50"
                >
                  <span className="material-symbols-outlined text-[16px]">lock_open</span>
                  Unlock {UNLOCK_MINUTES} min
                </button>
              </div>
            )}

            <button
              data-testid="extend-student-time"
              onClick={props.onExtendTime}
              className="w-full mb-2 py-2 bg-white border border-slate-200 hover:bg-slate-100 text-slate-700 text-xs font-bold rounded-xl transition-all flex items-center justify-center gap-1.5"
            >
              <span className="material-symbols-outlined text-[16px]">more_time</span>
              Extra Time for Student{(student.extraMinutes ?? 0) > 0 ? ` (+${student.extraMinutes} min)` : ''}
            </button>
            <button
              onClick={props.onForceSubmit}
              className="w-full py-2.5 bg-red-600 hover:bg-red-700 text-white text-xs font-bold rounded-xl shadow-sm hover:shadow transition-all active:scale-[0.98] flex items-center justify-center gap-1.5"
            >
              <span className="material-symbols-outlined text-[16px]">output</span>
              Force Submit Student
            </button>
          </>
        )}
      </div>
    </aside>
  );
}
