import { useState, useEffect, useMemo, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import AppLayout from '../../components/layout/AppLayout';
import { examsApi } from '../../api/exams.api';
import { labsApi } from '../../api/labs.api';
import type { AttendanceImportResult, Exam, ExamAssignment, LabInfo, ReadinessResult } from '../../types';

/** ✓ fresh pass · ✗ failed · ⌛ passed but older than 30 min · — never run */
function ReadinessBadge({ result }: { result?: ReadinessResult }) {
  const symbol = !result ? '—' : !result.passedAll ? '✗' : result.isFresh ? '✓' : '⌛';
  const color = !result ? 'text-slate-300' : !result.passedAll ? 'text-red-600' : result.isFresh ? 'text-emerald-600' : 'text-amber-500';
  const failed = result?.results.filter(r => !r.passed) ?? [];
  return (
    <span className="relative group inline-flex">
      <span data-testid="readiness-status" tabIndex={0} className={`font-bold text-base cursor-default ${color}`}>{symbol}</span>
      {result && (
        <span role="tooltip"
              className="hidden group-hover:block group-focus-within:block absolute z-20 left-6 top-0 w-64 p-3 rounded-lg bg-slate-800 text-white text-xs shadow-lg">
          <span className="block font-semibold mb-1">
            {result.passedAll ? (result.isFresh ? 'System check passed' : 'Passed, but expired (> 30 min)') : 'System check failed'}
          </span>
          {failed.map(f => <span key={f.name} className="block">✗ {f.name}: {f.detail || 'failed'}</span>)}
          <span className="block text-slate-400 mt-1">
            {new Date(result.checkedAt).toLocaleTimeString()}{result.machineName ? ` · ${result.machineName}` : ''}
          </span>
        </span>
      )}
    </span>
  );
}

/** "75%" (rounded to 2 dp, trailing zeros dropped) with "30/40 classes" underneath; "—" without a record. */
function AttendanceCell({ item, min }: { item: ExamAssignment; min?: number }) {
  if (item.totalClasses == null) return <span className="text-slate-300 text-sm" title="No attendance imported for this student">—</span>;
  if (item.attendancePercent == null) return <span className="text-amber-600 text-xs font-semibold" title="Zero classes recorded">0 classes</span>;
  const pct = Math.round(item.attendancePercent * 100) / 100;
  const below = min != null && item.attendancePercent < min;
  return (
    <div data-testid="attendance-cell">
      <span className={`font-bold text-sm ${below ? 'text-red-600' : 'text-emerald-700'}`}>{pct}%</span>
      <span className="block text-[10px] text-slate-400">{item.attendedClasses}/{item.totalClasses} classes</span>
    </div>
  );
}

type Baseline = Record<string, { isEligible: boolean; note: string }>;

const baselineOf = (rows: ExamAssignment[]): Baseline =>
  Object.fromEntries(rows.map(a => [a.userId, { isEligible: a.isEligible, note: a.eligibilityNote || '' }]));

export default function EligibilityPage() {
  const [exams, setExams] = useState<Exam[]>([]);
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedExamId, setSelectedExamId] = useState('');
  const [assignments, setAssignments] = useState<ExamAssignment[]>([]);
  // Server state of each row — Save sends only rows that differ, so untouched rows never become manual overrides.
  const [baseline, setBaseline] = useState<Baseline>({});
  const [editingNotes, setEditingNotes] = useState<Set<string>>(new Set());
  const [readiness, setReadiness] = useState<Record<string, ReadinessResult>>({});
  const [loading, setLoading] = useState(false);
  const [labs, setLabs] = useState<LabInfo[]>([]);
  const [sectionStudents, setSectionStudents] = useState<{ userId: string; name: string; email: string }[]>([]);
  const [addStudentId, setAddStudentId] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Attendance CSV dialog (Module 9)
  const [showImport, setShowImport] = useState(false);
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importResult, setImportResult] = useState<AttendanceImportResult | null>(null);
  const [importError, setImportError] = useState<string | null>(null);

  // Load all exams + labs initially
  useEffect(() => {
    examsApi.getAll()
      .then((data) => {
        setExams(data);
        // Prioritize URL param if present
        const urlExamId = searchParams.get('examId');
        if (urlExamId) {
          setSelectedExamId(urlExamId);
        } else if (data.length > 0) {
          setSelectedExamId(data[0].examId);
        }
      })
      .catch((err) => console.error('Failed to load exams:', err));
    labsApi.getAll()
      .then(setLabs)
      .catch((err) => console.error('Failed to load labs:', err));
  }, [searchParams]);

  const selectedExam = exams.find(e => e.examId === selectedExamId);
  const minAttendance = selectedExam?.minAttendancePercent;

  const loadAssignments = () => {
    if (!selectedExamId) return;
    setLoading(true);
    examsApi.getAssignments(selectedExamId)
      .then((data) => {
        setAssignments(data);
        setBaseline(baselineOf(data));
        setEditingNotes(new Set());
      })
      .catch((err) => console.error('Failed to load assignments:', err))
      .finally(() => setLoading(false));
    examsApi.getReadiness(selectedExamId)
      .then((rows) => setReadiness(Object.fromEntries(rows.map(r => [r.userId, r]))))
      .catch(() => setReadiness({}));
  };

  // Load assignments + enrolled section students when selected exam changes. Rows of the previous
  // exam are cleared on a switch; a reload of the same exam keeps the table mounted (no flashing,
  // and a row being edited isn't swapped for a spinner).
  const loadedExamId = useRef('');
  useEffect(() => {
    if (!selectedExamId) return;
    if (loadedExamId.current !== selectedExamId) {
      loadedExamId.current = selectedExamId;
      setAssignments([]);
      setBaseline({});
    }
    loadAssignments();
    setNotice(null);
    const exam = exams.find(e => e.examId === selectedExamId);
    if (exam?.sectionId) {
      examsApi.getSectionStudents(exam.sectionId)
        .then(setSectionStudents)
        .catch(() => setSectionStudents([]));
    } else {
      setSectionStudents([]);
    }
    setAddStudentId('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedExamId, exams]);

  const isDirty = (a: ExamAssignment) => {
    const b = baseline[a.userId];
    return !b || b.isEligible !== a.isEligible || b.note !== (a.eligibilityNote || '');
  };
  const dirtyRows = useMemo(() => assignments.filter(isDirty), [assignments, baseline]); // eslint-disable-line react-hooks/exhaustive-deps
  const hasChanges = dirtyRows.length > 0;

  // Section students not yet assigned to this exam (candidates for "Add Student")
  const unassignedStudents = sectionStudents.filter(
    s => !assignments.some(a => a.userId === s.userId)
  );

  const handleSeatChange = async (userId: string, workstationId: string) => {
    try {
      await examsApi.assignWorkstation(selectedExamId, userId, workstationId || null);
      loadAssignments();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to assign seat.');
    }
  };

  const handleAddStudent = async () => {
    if (!addStudentId) return;
    try {
      await examsApi.addStudent(selectedExamId, addStudentId);
      setAddStudentId('');
      loadAssignments();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to add student.');
    }
  };

  const handleRemoveStudent = async (a: ExamAssignment) => {
    if (!window.confirm(`Remove ${a.studentName} from this exam?`)) return;
    try {
      await examsApi.removeStudent(selectedExamId, a.userId);
      loadAssignments();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to remove student.');
    }
  };

  const toggleEligibility = (userId: string) => {
    setAssignments((prev) =>
      prev.map((a) => (a.userId === userId ? { ...a, isEligible: !a.isEligible } : a))
    );
  };

  const handleNoteChange = (userId: string, note: string) => {
    setAssignments((prev) =>
      prev.map((a) => (a.userId === userId ? { ...a, eligibilityNote: note } : a))
    );
  };

  const handleMarkAll = (eligible: boolean) => {
    setAssignments((prev) => prev.map((a) => ({ ...a, isEligible: eligible })));
  };

  const handleSave = async () => {
    if (!selectedExamId || !hasChanges) return;
    try {
      await examsApi.updateEligibility(
        selectedExamId,
        dirtyRows.map((a) => ({
          userId: a.userId,
          isEligible: a.isEligible,
          eligibilityNote: a.eligibilityNote || '',
        }))
      );
      alert('Eligibility parameters successfully updated.');
      loadAssignments();
    } catch (err) {
      console.error(err);
      alert('Failed to save eligibility changes.');
    }
  };

  const handleDiscard = () => loadAssignments();

  // Hand one student's decision back to the attendance rule (drops the manual override).
  const handleClearOverride = async (a: ExamAssignment) => {
    try {
      await examsApi.updateEligibility(selectedExamId, [{
        userId: a.userId, isEligible: a.isEligible, eligibilityNote: a.eligibilityNote || '', clearOverride: true,
      }]);
      setNotice(`${a.studentName} now follows attendance again — click Recalculate to apply it.`);
      loadAssignments();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to clear the override.');
    }
  };

  const handleRecalculate = async () => {
    if (!selectedExamId) return;
    if (hasChanges && !window.confirm('Recalculating discards your unsaved changes. Continue?')) return;
    setBusy(true);
    try {
      const r = await examsApi.recalculateEligibility(selectedExamId);
      setNotice(
        `Recalculated from attendance (minimum ${r.minAttendancePercent}%): ${r.computed} computed, ` +
        `${r.ineligible} ineligible, ${r.skippedOverridden} manual override(s) kept, ${r.notComputed} without attendance data.`
      );
      loadAssignments();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to recalculate eligibility.');
    } finally {
      setBusy(false);
    }
  };

  const openImport = () => {
    setImportFile(null);
    setImportResult(null);
    setImportError(null);
    setShowImport(true);
  };

  const handleImport = async () => {
    if (!importFile || !selectedExam?.sectionId) return;
    setBusy(true);
    setImportError(null);
    try {
      const result = await examsApi.importAttendance(selectedExam.sectionId, importFile);
      setImportResult(result);
      loadAssignments();
    } catch (err: any) {
      setImportError(err.response?.data?.message || 'Failed to import the attendance file.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <AppLayout title="Eligibility Management">
      <div className="space-y-6">
        {/* Header Control Row */}
        <div className="flex flex-col md:flex-row md:items-end justify-between gap-6">
          <div className="space-y-2">
            <label className="text-xs font-bold tracking-wider text-slate-400 uppercase block">Select Examination</label>
            <div className="relative w-full md:w-[320px]">
              <select
                value={selectedExamId}
                onChange={(e) => {
                  setSelectedExamId(e.target.value);
                  setSearchParams({ examId: e.target.value });
                }}
                className="w-full h-10 border border-slate-200 bg-white rounded-lg px-4 pr-10 focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none text-sm cursor-pointer shadow-sm"
              >
                <option value="">Choose an exam</option>
                {exams.map((exam) => (
                  <option key={exam.examId} value={exam.examId}>
                    {exam.title} ({exam.courseName})
                  </option>
                ))}
              </select>
            </div>
            {minAttendance != null && (
              <p className="text-xs text-slate-400">
                Minimum attendance: <span data-testid="min-attendance" className="font-bold text-slate-600">{minAttendance}%</span>
              </p>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-3">
            {/* Add a section student who isn't assigned yet (Module 9) */}
            {unassignedStudents.length > 0 && (
              <div className="flex items-center gap-2">
                <select
                  value={addStudentId}
                  onChange={(e) => setAddStudentId(e.target.value)}
                  className="h-10 border border-slate-200 bg-white rounded-lg px-3 text-xs focus:ring-2 focus:ring-primary-500 outline-none cursor-pointer"
                >
                  <option value="">Add student…</option>
                  {unassignedStudents.map((s) => (
                    <option key={s.userId} value={s.userId}>{s.name} ({s.email})</option>
                  ))}
                </select>
                <button
                  onClick={handleAddStudent}
                  disabled={!addStudentId}
                  className="px-4 h-10 bg-primary-500 text-white font-bold rounded-lg hover:bg-primary-600 transition-colors text-xs disabled:opacity-40"
                >
                  Add
                </button>
              </div>
            )}
            <div className="bg-blue-50 px-4 py-2 rounded-full flex items-center gap-2">
              <svg className="w-4.5 h-4.5 text-primary-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
              </svg>
              <span className="font-bold text-primary-600 text-sm">{assignments.length} Students</span>
            </div>
            <button
              onClick={openImport}
              disabled={!selectedExam?.sectionId}
              className="px-4 h-10 border border-slate-200 text-slate-600 font-semibold rounded-lg hover:bg-slate-50 transition-colors text-xs disabled:opacity-40"
            >
              Upload Attendance
            </button>
            <button
              onClick={handleRecalculate}
              disabled={!selectedExamId || busy}
              title="Set eligibility from imported attendance; manual overrides are kept"
              className="px-4 h-10 bg-primary-500 text-white font-bold rounded-lg hover:bg-primary-600 transition-colors text-xs disabled:opacity-40"
            >
              Recalculate
            </button>
            <button
              onClick={() => handleMarkAll(true)}
              className="px-4 h-10 bg-blue-50 hover:bg-blue-100 text-primary-600 font-bold rounded-lg transition-colors flex items-center gap-2 text-xs"
            >
              Mark All Eligible
            </button>
            <button
              onClick={() => handleMarkAll(false)}
              className="px-4 h-10 border border-slate-200 text-slate-500 font-semibold rounded-lg hover:bg-slate-50 transition-colors flex items-center gap-2 text-xs"
            >
              Mark All Ineligible
            </button>
          </div>
        </div>

        {notice && (
          <div role="status" className="flex items-start justify-between gap-4 bg-blue-50 border border-blue-100 text-blue-800 text-sm rounded-lg px-4 py-3">
            <span>{notice}</span>
            <button onClick={() => setNotice(null)} className="text-blue-400 hover:text-blue-700 text-xs font-bold" aria-label="Dismiss">✕</button>
          </div>
        )}

        {/* Table View */}
        {loading && assignments.length === 0 ? (
          <div className="min-h-[300px] flex items-center justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
          <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200">
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Student Name</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Email</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Workstation</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">System Check</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Attendance</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Eligibility</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Note / Reason</th>
                    <th className="px-6 py-4 w-14"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {assignments.length === 0 ? (
                    <tr>
                      <td colSpan={8} className="px-6 py-12 text-center text-sm text-slate-400">
                        {selectedExamId ? 'No student assignments registered.' : 'Please select an exam.'}
                      </td>
                    </tr>
                  ) : (
                    assignments.map((item) => {
                      // A decision made by "Recalculate" shows its note as text; editing it (or the
                      // switch) turns the row into a manual override on save.
                      const computedNote = item.eligibilitySource === 'Attendance' && !item.eligibilityOverridden &&
                        !isDirty(item) && !editingNotes.has(item.userId);
                      return (
                      <tr
                        key={item.userId}
                        className={`transition-colors hover:bg-slate-50/50 ${
                          !item.isEligible ? 'bg-red-50/20 hover:bg-red-50/30' : ''
                        }`}
                      >
                        <td className="px-6 py-4 flex items-center gap-3">
                          <div className={`w-8 h-8 rounded-full flex items-center justify-center font-bold text-xs ${
                            item.isEligible ? 'bg-blue-50 text-primary-500' : 'bg-red-100 text-red-600'
                          }`}>
                            {item.studentName.split(' ').map((n: string) => n[0]).join('')}
                          </div>
                          <span className={`font-semibold text-sm ${!item.isEligible ? 'text-red-700' : 'text-slate-700'}`}>
                            {item.studentName}
                          </span>
                        </td>
                        <td className="px-6 py-4 text-sm text-slate-400">{item.studentEmail}</td>
                        <td className="px-6 py-4">
                          {/* Seat assignment (Module 9): server rejects double-booked seats */}
                          <select
                            value={item.workstationId || ''}
                            onChange={(e) => handleSeatChange(item.userId, e.target.value)}
                            className="font-mono text-xs border border-slate-200 rounded-lg px-2 py-1.5 bg-white text-slate-600 focus:ring-2 focus:ring-primary-500 outline-none cursor-pointer"
                          >
                            <option value="">Not Mapped</option>
                            {labs.map((lab) =>
                              lab.workstations.map((ws) => (
                                <option key={ws.workstationId} value={ws.workstationId}>
                                  {ws.machineNumber} — {lab.name}{ws.ipAddress ? ` (${ws.ipAddress})` : ''}
                                </option>
                              ))
                            )}
                          </select>
                        </td>
                        <td className="px-6 py-4">
                          <ReadinessBadge result={readiness[item.userId]} />
                        </td>
                        <td className="px-6 py-4">
                          <AttendanceCell item={item} min={minAttendance} />
                        </td>
                        <td className="px-6 py-4">
                          <div className="flex items-center gap-2">
                            <label className="relative inline-flex items-center cursor-pointer">
                              <input
                                type="checkbox"
                                role="switch"
                                aria-checked={item.isEligible ? "true" : "false"}
                                checked={item.isEligible}
                                onChange={() => toggleEligibility(item.userId)}
                                className="sr-only peer"
                              />
                              <div className="w-10 h-5 bg-slate-200 rounded-full peer peer-checked:after:translate-x-full after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-primary-500"></div>
                            </label>
                            {item.eligibilityOverridden ? (
                              <span data-testid="eligibility-source" className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-amber-50 border border-amber-200 text-amber-700 text-[10px] font-bold uppercase"
                                    title="Set manually — Recalculate leaves this student alone">
                                Manual
                                <button
                                  onClick={() => handleClearOverride(item)}
                                  title="Clear override — follow attendance again"
                                  aria-label={`Clear override for ${item.studentName}`}
                                  className="text-amber-500 hover:text-amber-800"
                                >↺</button>
                              </span>
                            ) : item.eligibilitySource === 'Attendance' ? (
                              <span data-testid="eligibility-source" className="px-2 py-0.5 rounded-full bg-slate-100 text-slate-500 text-[10px] font-bold uppercase"
                                    title="Computed from attendance">
                                Attendance
                              </span>
                            ) : null}
                          </div>
                        </td>
                        <td className="px-6 py-4">
                          {computedNote ? (
                            <button
                              type="button"
                              onClick={() => setEditingNotes(prev => new Set(prev).add(item.userId))}
                              title="Edit to override"
                              className={`text-left text-sm ${!item.isEligible ? 'text-red-700 font-semibold' : 'text-slate-500'}`}
                            >
                              {item.eligibilityNote}
                            </button>
                          ) : (
                            <input
                              type="text"
                              placeholder="Add reason/note..."
                              value={item.eligibilityNote || ''}
                              onChange={(e) => handleNoteChange(item.userId, e.target.value)}
                              className={`w-full bg-transparent border-none p-0 focus:ring-0 text-sm ${
                                !item.isEligible ? 'text-red-700 font-semibold' : 'text-slate-500 italic'
                              }`}
                            />
                          )}
                        </td>
                        <td className="px-6 py-4 text-right">
                          <button
                            onClick={() => handleRemoveStudent(item)}
                            title="Remove student from this exam"
                            className="p-1.5 rounded-lg text-slate-300 hover:text-red-500 hover:bg-red-50 transition-colors"
                          >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                            </svg>
                          </button>
                        </td>
                      </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {/* Attendance CSV upload dialog (Module 9) */}
      {showImport && (
        <div className="fixed inset-0 z-50 bg-slate-900/40 flex items-center justify-center p-4">
          <div role="dialog" aria-modal="true" aria-labelledby="attendance-dialog-title" className="bg-white rounded-xl shadow-xl w-full max-w-lg p-6 space-y-4">
            <div>
              <h3 id="attendance-dialog-title" className="text-lg font-bold text-slate-800">Upload attendance</h3>
              <p className="text-xs text-slate-500 mt-1">
                CSV with a header row containing <span className="font-mono">email</span>, <span className="font-mono">total</span> and{' '}
                <span className="font-mono">attended</span> (any order). Applies to every exam of section{' '}
                <span className="font-semibold">{selectedExam?.sectionName || selectedExam?.courseName}</span>; re-uploading updates existing rows.
              </p>
            </div>
            <div className="space-y-1.5">
              <label htmlFor="attendance-file" className="text-xs font-bold tracking-wider text-slate-400 uppercase block">CSV file</label>
              <input
                id="attendance-file"
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => { setImportFile(e.target.files?.[0] ?? null); setImportResult(null); setImportError(null); }}
                className="block w-full text-sm text-slate-600 file:mr-3 file:px-3 file:py-1.5 file:rounded-lg file:border-0 file:bg-blue-50 file:text-primary-600 file:font-semibold"
              />
            </div>

            {importError && <p role="alert" className="text-sm text-red-600">{importError}</p>}
            {importResult && (
              <div data-testid="import-result" className="text-sm bg-slate-50 border border-slate-100 rounded-lg p-3 space-y-1">
                <p className="font-semibold text-slate-700">
                  {importResult.imported} added, {importResult.updated} updated, {importResult.errors.length} rejected.
                  {importResult.imported + importResult.updated > 0 && ' Click Recalculate to apply the attendance rule.'}
                </p>
                {importResult.errors.slice(0, 8).map(e => (
                  <p key={`e${e.line}`} className="text-xs text-red-600">Line {e.line}: {e.email || '(no email)'} — {e.reason}</p>
                ))}
                {importResult.warnings.slice(0, 8).map(w => (
                  <p key={`w${w.line}`} className="text-xs text-amber-600">Line {w.line}: {w.email} — {w.reason}</p>
                ))}
              </div>
            )}

            <div className="flex justify-end gap-3 pt-2">
              <button onClick={() => setShowImport(false)} className="px-5 h-10 text-slate-500 hover:text-slate-800 text-sm font-semibold">
                {importResult ? 'Done' : 'Cancel'}
              </button>
              <button
                onClick={handleImport}
                disabled={!importFile || busy}
                className="px-6 h-10 bg-primary-500 text-white font-bold text-sm rounded-lg hover:bg-primary-600 disabled:opacity-40"
              >
                Import
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Floating Save Footer */}
      {hasChanges && (
        <footer className="fixed bottom-0 left-[240px] right-0 bg-white/90 backdrop-blur-md border-t border-slate-200 px-8 py-4 z-40 flex justify-between items-center shadow-lg animate-fade-in">
          <div className="flex items-center gap-3">
            <div className="relative">
              <div className="w-3 h-3 bg-orange-500 rounded-full animate-pulse"></div>
              <div className="absolute inset-0 w-3 h-3 bg-orange-400 rounded-full scale-150 opacity-20"></div>
            </div>
            <p className="text-slate-800 text-sm font-semibold">Unsaved changes in eligibility list ({dirtyRows.length} student{dirtyRows.length === 1 ? '' : 's'})</p>
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={handleDiscard}
              className="px-6 h-10 text-slate-400 hover:text-slate-700 text-sm font-semibold transition-colors"
            >
              Discard
            </button>
            <button
              onClick={handleSave}
              className="px-8 h-10 bg-primary-500 text-white font-bold text-sm rounded-lg hover:bg-primary-600 shadow-lg shadow-blue-200 transition-all active:scale-[0.98]"
            >
              Save Changes
            </button>
          </div>
        </footer>
      )}
    </AppLayout>
  );
}
