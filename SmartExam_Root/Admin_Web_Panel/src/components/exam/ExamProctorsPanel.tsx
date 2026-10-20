import { useEffect, useMemo, useState } from 'react';
import { examsApi } from '../../api/exams.api';
import { usersApi } from '../../api/users.api';
import type { ExamProctor, User } from '../../types';

/**
 * Module 7 — who supervises an exam besides its teacher. The section's teacher (or an admin)
 * assigns invigilators (or other teachers); an assignment is what lets an invigilator see the exam
 * in their live monitor at all. The server validates every change (role, ownership, duplicates).
 */
export default function ExamProctorsPanel({ examId }: { examId: string }) {
  const [proctors, setProctors] = useState<ExamProctor[]>([]);
  const [staff, setStaff] = useState<User[]>([]);
  const [selected, setSelected] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadProctors = () =>
    examsApi.getProctors(examId)
      .then(setProctors)
      .catch(err => setError(err.response?.data?.message || 'Could not load invigilators.'));

  useEffect(() => {
    setError(null);
    setSelected('');
    loadProctors();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [examId]);

  useEffect(() => {
    usersApi.getAll()
      .then(users => setStaff(users.filter(u => u.isActive && (u.role === 'Invigilator' || u.role === 'Teacher'))))
      .catch(() => setStaff([]));
  }, []);

  // Invigilators first, then teachers (co-proctors); never someone already assigned.
  const candidates = useMemo(() => {
    const assigned = new Set(proctors.map(p => p.userId));
    return staff
      .filter(u => !assigned.has(u.userId))
      .sort((a, b) => (a.role === b.role ? a.name.localeCompare(b.name) : a.role === 'Invigilator' ? -1 : 1));
  }, [staff, proctors]);

  const handleAssign = async () => {
    if (!selected) return;
    setBusy(true);
    setError(null);
    try {
      await examsApi.assignProctor(examId, selected);
      setSelected('');
      await loadProctors();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Could not assign this person.');
    } finally {
      setBusy(false);
    }
  };

  const handleRemove = async (p: ExamProctor) => {
    if (!window.confirm(`Remove ${p.name} as an invigilator of this exam?`)) return;
    setBusy(true);
    setError(null);
    try {
      await examsApi.unassignProctor(examId, p.userId);
      await loadProctors();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Could not remove this invigilator.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <section data-testid="proctors-panel" className="bg-white border border-slate-200 rounded-xl px-5 py-4 shadow-sm">
      <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-3">
        <div className="min-w-0">
          <h4 className="text-sm font-bold text-slate-800">Invigilators</h4>
          <p className="text-xs text-slate-400">Assigned invigilators see only this exam in their live monitor.</p>
          <div className="flex flex-wrap gap-2 mt-2">
            {proctors.length === 0 && <span className="text-xs text-slate-400 italic">No invigilator assigned.</span>}
            {proctors.map(p => (
              <span key={p.userId} data-testid="proctor-chip"
                    className="inline-flex items-center gap-1.5 pl-2.5 pr-1 py-0.5 rounded-full bg-amber-50 border border-amber-200 text-amber-800 text-xs font-semibold">
                {p.name}
                {p.role === 'Teacher' && <span className="text-[9px] uppercase tracking-wider text-amber-600">teacher</span>}
                <button onClick={() => handleRemove(p)} disabled={busy} aria-label={`Remove ${p.name}`}
                        className="w-5 h-5 rounded-full hover:bg-amber-100 text-amber-700 leading-none disabled:opacity-40">×</button>
              </span>
            ))}
          </div>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <select data-testid="proctor-select" aria-label="Invigilator to assign" value={selected} onChange={e => setSelected(e.target.value)}
                  className="h-9 max-w-[260px] border border-slate-200 bg-white rounded-lg px-3 text-xs focus:ring-2 focus:ring-primary-500 outline-none cursor-pointer">
            <option value="">Choose invigilator…</option>
            {candidates.map(u => (
              <option key={u.userId} value={u.userId}>{u.name} ({u.role === 'Teacher' ? 'teacher' : u.email})</option>
            ))}
          </select>
          <button onClick={handleAssign} disabled={!selected || busy}
                  className="px-4 h-9 bg-amber-500 text-white font-bold rounded-lg hover:bg-amber-600 transition-colors text-xs disabled:opacity-40">
            Assign Invigilator
          </button>
        </div>
      </div>
      {error && <p role="alert" className="mt-2 text-xs text-red-600">{error}</p>}
    </section>
  );
}
