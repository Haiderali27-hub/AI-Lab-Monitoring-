import { useEffect, useMemo, useState } from 'react'
import {
  archiveExam,
  cancelExam,
  createExam,
  deleteExam,
  getExamAssignments,
  getExamCandidates,
  getExamProctors,
  getExams,
  updateExam,
  updateExamAssignments,
} from '../api/exams'
import { getLabs, getWorkstations } from '../api/institution'
import { AdminLayout } from '../components/AdminLayout'
import type {
  CreateExamPayload,
  ExamAssignmentInput,
  ExamCandidate,
  ExamSummary,
  Lab,
  Proctor,
  Workstation,
} from '../types'

type Mode = 'list' | 'create' | 'edit'
type StatusFilter = 'All' | 'Scheduled' | 'Live' | 'Completed' | 'Cancelled' | 'Archived' | 'Draft'

const STATUS_FILTERS: StatusFilter[] = ['All', 'Scheduled', 'Live', 'Completed', 'Cancelled', 'Archived', 'Draft']

function statusBadgeClass(status: string): string {
  switch (status) {
    case 'Live': return 'active'
    case 'Scheduled': return 'secure'
    case 'Completed': return 'secure'
    case 'Cancelled': return 'danger'
    case 'Archived': return 'muted'
    default: return 'secure'
  }
}

function toDateInput(value: string): string {
  return new Date(value).toISOString().slice(0, 10)
}

function toTimeInput(value: string): string {
  return new Date(value).toTimeString().slice(0, 5)
}

function minutesBetween(startUtc: string, endUtc: string): number {
  return Math.max(15, Math.round((new Date(endUtc).getTime() - new Date(startUtc).getTime()) / 60000))
}

function toUtcIso(dateValue: string, timeValue: string): string {
  return new Date(`${dateValue}T${timeValue}`).toISOString()
}

export default function Exams() {
  const [mode, setMode] = useState<Mode>('list')
  const [editingExamId, setEditingExamId] = useState<string | null>(null)
  const [exams, setExams] = useState<ExamSummary[]>([])
  const [candidates, setCandidates] = useState<ExamCandidate[]>([])
  const [labs, setLabs] = useState<Lab[]>([])
  const [workstations, setWorkstations] = useState<Workstation[]>([])
  const [proctors, setProctors] = useState<Proctor[]>([])
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All')

  const [name, setName] = useState('')
  const [date, setDate] = useState('')
  const [startTime, setStartTime] = useState('09:00')
  const [duration, setDuration] = useState(120)
  const [labId, setLabId] = useState<string | null>(null)
  const [proctorUserId, setProctorUserId] = useState<string | null>(null)
  const [instructions, setInstructions] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [search, setSearch] = useState('')
  const [assignments, setAssignments] = useState<Record<string, ExamAssignmentInput>>({})

  const filteredCandidates = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return candidates
    return candidates.filter((c) => `${c.username} ${c.email}`.toLowerCase().includes(term))
  }, [candidates, search])

  const selectedAssignments = useMemo(() => Object.values(assignments), [assignments])
  const selectedLab = useMemo(() => labs.find((l) => l.id === labId) ?? null, [labs, labId])
  const availableWorkstations = useMemo(() => workstations.filter((w) => w.isActive), [workstations])

  const visibleExams = useMemo(() => {
    if (statusFilter === 'All') return exams
    return exams.filter((e) => e.status === statusFilter)
  }, [exams, statusFilter])

  async function loadStaticData() {
    const [examResult, candidateResult, labResult, proctorResult] = await Promise.all([
      getExams(),
      getExamCandidates(),
      getLabs(),
      getExamProctors(),
    ])
    setExams(examResult)
    setCandidates(candidateResult)
    setLabs(labResult)
    setProctors(proctorResult)
  }

  async function loadWorkstationsForLab(nextLabId: string | null) {
    if (!nextLabId) { setWorkstations([]); return }
    setWorkstations(await getWorkstations(nextLabId))
  }

  useEffect(() => {
    void loadStaticData().catch((err) => setError(err instanceof Error ? err.message : 'Failed to load exam data.'))
  }, [])

  useEffect(() => {
    void loadWorkstationsForLab(labId).catch((err) => setError(err instanceof Error ? err.message : 'Failed to load lab machines.'))
    setAssignments((cur) => {
      const next: Record<string, ExamAssignmentInput> = {}
      Object.entries(cur).forEach(([sid, a]) => { next[sid] = { ...a, workstationId: null } })
      return next
    })
  }, [labId])

  function resetForm() {
    setEditingExamId(null); setName(''); setDate(''); setStartTime('09:00')
    setDuration(120); setLabId(null); setProctorUserId(null)
    setInstructions(''); setIsActive(true); setSearch(''); setAssignments({}); setError(null)
  }

  function startCreate() { resetForm(); setMode('create') }

  async function startEdit(exam: ExamSummary) {
    setBusy(true); setError(null)
    try {
      setEditingExamId(exam.id); setName(exam.name)
      setDate(toDateInput(exam.startUtc)); setStartTime(toTimeInput(exam.startUtc))
      setDuration(minutesBetween(exam.startUtc, exam.endUtc))
      setLabId(exam.labId); setProctorUserId(exam.proctorUserId)
      setInstructions(exam.instructions ?? ''); setIsActive(exam.isActive)
      await loadWorkstationsForLab(exam.labId)
      const existing = await getExamAssignments(exam.id)
      const next: Record<string, ExamAssignmentInput> = {}
      existing.forEach((a) => { next[a.studentId] = { studentId: a.studentId, isEligible: a.isEligible, workstationId: a.workstationId } })
      setAssignments(next); setMode('edit')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load exam.')
    } finally { setBusy(false) }
  }

  function toggleStudent(studentId: string, checked: boolean) {
    setAssignments((cur) => {
      const next = { ...cur }
      if (!checked) { delete next[studentId] } else { next[studentId] = { studentId, isEligible: true, workstationId: null } }
      return next
    })
  }

  function updateStudentAssignment(studentId: string, patch: Partial<ExamAssignmentInput>) {
    setAssignments((cur) => {
      const existing = cur[studentId]
      if (!existing) return cur
      return { ...cur, [studentId]: { ...existing, ...patch } }
    })
  }

  async function onSaveExam() {
    if (!name.trim() || !date || !startTime || duration <= 0) {
      setError('Complete exam title, date, start time, and duration.'); return
    }
    setBusy(true); setError(null); setNotice(null)
    try {
      const startUtc = toUtcIso(date, startTime)
      const endDate = new Date(`${date}T${startTime}`)
      endDate.setMinutes(endDate.getMinutes() + duration)
      const endUtc = endDate.toISOString()
      const basePayload = { name: name.trim(), startUtc, endUtc, labId, proctorUserId, instructions: instructions.trim() || null }
      if (mode === 'edit' && editingExamId) {
        await updateExam(editingExamId, { ...basePayload, isActive })
        await updateExamAssignments(editingExamId, selectedAssignments)
        setNotice('Exam updated.')
      } else {
        const payload: CreateExamPayload = { ...basePayload, assignments: selectedAssignments }
        await createExam(payload)
        setNotice('Exam created.')
      }
      await loadStaticData(); resetForm(); setMode('list')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save exam.')
    } finally { setBusy(false) }
  }

  async function onCancelExam(exam: ExamSummary) {
    if (!window.confirm(`Cancel exam "${exam.name}"? Active student sessions will be terminated.`)) return
    setBusy(true); setError(null)
    try {
      await cancelExam(exam.id)
      await loadStaticData()
      setNotice(`Exam "${exam.name}" has been cancelled.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cancel exam.')
    } finally { setBusy(false) }
  }

  async function onArchiveExam(exam: ExamSummary) {
    const willArchive = !exam.isArchived
    setBusy(true); setError(null)
    try {
      await archiveExam(exam.id, willArchive)
      await loadStaticData()
      setNotice(willArchive ? `Exam "${exam.name}" archived.` : `Exam "${exam.name}" unarchived.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to archive exam.')
    } finally { setBusy(false) }
  }

  async function onDeleteExam(exam: ExamSummary) {
    if (!window.confirm(`Permanently delete exam "${exam.name}"? This cannot be undone.`)) return
    setBusy(true); setError(null)
    try {
      await deleteExam(exam.id)
      await loadStaticData()
      setNotice(`Exam "${exam.name}" deleted.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete exam.')
    } finally { setBusy(false) }
  }

  // ── Create / Edit Form ──────────────────────────────────────────────────────
  if (mode !== 'list') {
    return (
      <AdminLayout>
        <div className="dashboard-content">
          <header className="page-header">
            <div className="header-text">
              <p className="eyebrow">Exam Configuration &amp; Scheduling</p>
              <h1>{mode === 'edit' ? 'Edit Exam' : 'Create New Exam'}</h1>
              <p className="subtext">Set the schedule, assign a proctor, select candidates, and map students to lab machines.</p>
            </div>
            <div className="header-actions" style={{ display: 'flex', gap: '0.75rem' }}>
              <button className="secondary-btn" type="button" onClick={() => { resetForm(); setMode('list') }}>Cancel</button>
              <button className="primary-btn" type="button" onClick={() => void onSaveExam()} disabled={busy}>
                {busy ? 'Saving...' : mode === 'edit' ? 'Save Changes' : 'Publish Exam'}
              </button>
            </div>
          </header>

          {notice && <div className="inline-alert">{notice}</div>}
          {error && <div className="inline-alert error">{error}</div>}

          <div className="exam-grid">
            <div className="exam-main">
              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">edit_note</span>
                  <h3>Exam Details</h3>
                </div>
                <div className="form-grid-2">
                  <div className="input-group" style={{ gridColumn: 'span 2' }}>
                    <label className="input-label">Exam Title</label>
                    <input className="input-control" value={name} onChange={(e) => setName(e.target.value)} placeholder="Database Systems Midterm" />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Date</label>
                    <input className="input-control" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Start Time</label>
                    <input className="input-control" type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Duration (minutes)</label>
                    <input className="input-control" type="number" min={15} value={duration} onChange={(e) => setDuration(Number(e.target.value))} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Exam State</label>
                    <select className="input-control select-control" value={isActive ? 'active' : 'draft'} onChange={(e) => setIsActive(e.target.value === 'active')}>
                      <option value="active">Active / Scheduled</option>
                      <option value="draft">Draft / Disabled</option>
                    </select>
                  </div>
                  <div className="input-group" style={{ gridColumn: 'span 2' }}>
                    <label className="input-label">Instructions</label>
                    <textarea className="input-control" rows={4} value={instructions} onChange={(e) => setInstructions(e.target.value)} placeholder="Exam rules, allowed tools, and submission instructions..." />
                  </div>
                </div>
              </section>

              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">settings_input_component</span>
                  <h3>Lab &amp; Proctor</h3>
                </div>
                <div className="form-grid-2">
                  <div className="input-group">
                    <label className="input-label">Lab</label>
                    <select className="input-control select-control" value={labId ?? ''} onChange={(e) => setLabId(e.target.value || null)}>
                      <option value="">No lab selected</option>
                      {labs.map((l) => <option key={l.id} value={l.id}>{l.name} ({l.registeredTerminals} terminals)</option>)}
                    </select>
                  </div>
                  <div className="input-group">
                    <label className="input-label">Teacher Proctor</label>
                    <select className="input-control select-control" value={proctorUserId ?? ''} onChange={(e) => setProctorUserId(e.target.value || null)}>
                      <option value="">No proctor selected</option>
                      {proctors.map((p) => <option key={p.id} value={p.id}>{p.username}</option>)}
                    </select>
                  </div>
                </div>
              </section>

              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">group_add</span>
                  <h3>Student Assignment &amp; Workstations</h3>
                </div>
                <div className="search-bar-v2">
                  <span className="material-symbols-outlined">search</span>
                  <input placeholder="Search students..." value={search} onChange={(e) => setSearch(e.target.value)} />
                </div>
                <div className="student-list" style={{ maxHeight: 520 }}>
                  {filteredCandidates.map((candidate) => {
                    const assignment = assignments[candidate.id]
                    return (
                      <div key={candidate.id} className="student-item" style={{ alignItems: 'stretch', gap: '1rem' }}>
                        <div className="student-item-left" style={{ minWidth: 220 }}>
                          <input type="checkbox" checked={Boolean(assignment)} onChange={(e) => toggleStudent(candidate.id, e.target.checked)} />
                          <div className="student-details">
                            <span className="student-name">{candidate.username}</span>
                            <span className="student-email">{candidate.email}</span>
                          </div>
                        </div>
                        <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                          <span className={`status-pill ${candidate.hasBinding ? 'bound' : 'unbound'}`}>{candidate.hasBinding ? 'Device Bound' : 'Unbound'}</span>
                          {assignment && (
                            <>
                              <select className="input-control select-control" style={{ width: 190 }} value={assignment.isEligible ? 'yes' : 'no'} onChange={(e) => updateStudentAssignment(candidate.id, { isEligible: e.target.value === 'yes' })}>
                                <option value="yes">Eligible</option>
                                <option value="no">Ineligible</option>
                              </select>
                              <select className="input-control select-control" style={{ width: 220 }} value={assignment.workstationId ?? ''} onChange={(e) => updateStudentAssignment(candidate.id, { workstationId: e.target.value || null })} disabled={!labId}>
                                <option value="">No workstation</option>
                                {availableWorkstations.map((w) => <option key={w.id} value={w.id}>{w.name}{w.ipAddress ? ` - ${w.ipAddress}` : ''}</option>)}
                              </select>
                            </>
                          )}
                        </div>
                      </div>
                    )
                  })}
                  {filteredCandidates.length === 0 && <div className="empty-state">No students match your search.</div>}
                </div>
              </section>
            </div>

            <aside className="exam-sidebar">
              <section className="glass-card assignment-summary-card">
                <h3>Assignment Summary</h3>
                <div className="summary-stat">
                  <p className="summary-stat-label">Assigned Students</p>
                  <p className="summary-stat-value">{selectedAssignments.length}</p>
                </div>
                <div className="capacity-info">
                  <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
                    <span>Lab Capacity</span>
                    <span>{selectedLab ? `${selectedAssignments.length}/${selectedLab.registeredTerminals}` : 'No lab'}</span>
                  </div>
                  <div className="capacity-bar">
                    <div className="capacity-fill" style={{ width: selectedLab ? `${Math.min(100, (selectedAssignments.length / Math.max(selectedLab.registeredTerminals, 1)) * 100)}%` : '0%' }} />
                  </div>
                </div>
                <div style={{ display: 'grid', gap: '0.75rem', marginTop: '1rem' }}>
                  <span className="status-pill bound">{selectedAssignments.filter((a) => a.isEligible).length} eligible</span>
                  <span className="status-pill unbound">{selectedAssignments.filter((a) => !a.isEligible).length} ineligible</span>
                  <span className="status-pill active">{selectedAssignments.filter((a) => a.workstationId).length} machines mapped</span>
                </div>
              </section>
            </aside>
          </div>
        </div>
      </AdminLayout>
    )
  }

  // ── List View ───────────────────────────────────────────────────────────────
  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Exam Configuration &amp; Scheduling</p>
            <h1>Exams &amp; Assessments</h1>
            <p className="subtext">Create exams, assign students, and manage the full exam lifecycle.</p>
          </div>
          <div style={{ display: 'flex', gap: '0.75rem' }}>
            <button className="secondary-btn" type="button" onClick={() => void loadStaticData()}>
              <span className="material-symbols-outlined">refresh</span>Refresh
            </button>
            <button className="primary-btn" type="button" onClick={startCreate}>
              <span className="material-symbols-outlined">add</span>Create Exam
            </button>
          </div>
        </header>

        {notice && <div className="inline-alert">{notice}</div>}
        {error && <div className="inline-alert error">{error}</div>}

        {/* Status filter tabs */}
        <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', marginBottom: '1rem' }}>
          {STATUS_FILTERS.map((filter) => {
            const count = filter === 'All' ? exams.length : exams.filter((e) => e.status === filter).length
            return (
              <button
                key={filter}
                type="button"
                className={statusFilter === filter ? 'primary-btn' : 'secondary-btn'}
                style={{ padding: '0.35rem 0.85rem', fontSize: '0.8rem' }}
                onClick={() => setStatusFilter(filter)}
              >
                {filter} <span style={{ opacity: 0.7, marginLeft: 4 }}>({count})</span>
              </button>
            )
          })}
        </div>

        <section className="glass-card table-container">
          <div className="table-header-row">
            <h3>Exam Schedule</h3>
            <span className="status-badge secure">{visibleExams.length} exams</span>
          </div>
          <div className="table-wrapper">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Exam</th>
                  <th>Schedule</th>
                  <th>Lab</th>
                  <th>Proctor</th>
                  <th>Candidates</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {visibleExams.map((exam) => (
                  <tr key={exam.id} className="table-row">
                    <td>
                      <div className="student-details">
                        <span className="student-name">{exam.name}</span>
                        <span className="student-email">{exam.instructions ?? 'No instructions'}</span>
                      </div>
                    </td>
                    <td>
                      <div className="student-details">
                        <span className="student-name">{new Date(exam.startUtc).toLocaleDateString()}</span>
                        <span className="student-email">{new Date(exam.startUtc).toLocaleTimeString()} – {new Date(exam.endUtc).toLocaleTimeString()}</span>
                      </div>
                    </td>
                    <td>{exam.labName ?? 'Unassigned'}</td>
                    <td>{exam.proctorName ?? 'Unassigned'}</td>
                    <td>{exam.eligibleCount}/{exam.candidateCount}</td>
                    <td>
                      <span className={`status-badge ${statusBadgeClass(exam.status)}`}>{exam.status}</span>
                    </td>
                    <td style={{ display: 'flex', gap: '0.25rem', flexWrap: 'wrap' }}>
                      {/* Edit — only if not cancelled/archived */}
                      {!exam.isCancelled && !exam.isArchived && (
                        <button className="icon-action-btn" type="button" title="Edit" onClick={() => void startEdit(exam)} disabled={busy}>
                          <span className="material-symbols-outlined">edit</span>
                        </button>
                      )}
                      {/* Cancel — only active/scheduled exams */}
                      {!exam.isCancelled && !exam.isArchived && (
                        <button className="icon-action-btn" type="button" title="Cancel exam" onClick={() => void onCancelExam(exam)} disabled={busy} style={{ color: 'var(--color-warning, #f59e0b)' }}>
                          <span className="material-symbols-outlined">cancel</span>
                        </button>
                      )}
                      {/* Archive / Unarchive */}
                      <button className="icon-action-btn" type="button" title={exam.isArchived ? 'Unarchive' : 'Archive'} onClick={() => void onArchiveExam(exam)} disabled={busy}>
                        <span className="material-symbols-outlined">{exam.isArchived ? 'unarchive' : 'archive'}</span>
                      </button>
                      {/* Delete — only draft, cancelled, or archived */}
                      {(exam.status === 'Draft' || exam.isCancelled || exam.isArchived) && (
                        <button className="icon-action-btn danger" type="button" title="Delete permanently" onClick={() => void onDeleteExam(exam)} disabled={busy}>
                          <span className="material-symbols-outlined">delete</span>
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {visibleExams.length === 0 && <p className="empty-state">No exams match the selected filter.</p>}
          </div>
        </section>
      </div>
    </AdminLayout>
  )
}
