import { useEffect, useMemo, useState } from 'react'
import {
  createExam,
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
    return candidates.filter((candidate) => `${candidate.username} ${candidate.email}`.toLowerCase().includes(term))
  }, [candidates, search])

  const selectedAssignments = useMemo(() => Object.values(assignments), [assignments])
  const selectedLab = useMemo(() => labs.find((lab) => lab.id === labId) ?? null, [labs, labId])
  const availableWorkstations = useMemo(() => workstations.filter((machine) => machine.isActive), [workstations])

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
    if (!nextLabId) {
      setWorkstations([])
      return
    }
    setWorkstations(await getWorkstations(nextLabId))
  }

  useEffect(() => {
    void loadStaticData().catch((err) => setError(err instanceof Error ? err.message : 'Failed to load exam data.'))
  }, [])

  useEffect(() => {
    void loadWorkstationsForLab(labId).catch((err) => setError(err instanceof Error ? err.message : 'Failed to load lab machines.'))
    setAssignments((current) => {
      const next: Record<string, ExamAssignmentInput> = {}
      Object.entries(current).forEach(([studentId, assignment]) => {
        next[studentId] = { ...assignment, workstationId: null }
      })
      return next
    })
  }, [labId])

  function resetForm() {
    setEditingExamId(null)
    setName('')
    setDate('')
    setStartTime('09:00')
    setDuration(120)
    setLabId(null)
    setProctorUserId(null)
    setInstructions('')
    setIsActive(true)
    setSearch('')
    setAssignments({})
    setError(null)
  }

  function startCreate() {
    resetForm()
    setMode('create')
  }

  async function startEdit(exam: ExamSummary) {
    setBusy(true)
    setError(null)
    try {
      setEditingExamId(exam.id)
      setName(exam.name)
      setDate(toDateInput(exam.startUtc))
      setStartTime(toTimeInput(exam.startUtc))
      setDuration(minutesBetween(exam.startUtc, exam.endUtc))
      setLabId(exam.labId)
      setProctorUserId(exam.proctorUserId)
      setInstructions(exam.instructions ?? '')
      setIsActive(exam.isActive)
      await loadWorkstationsForLab(exam.labId)
      const existingAssignments = await getExamAssignments(exam.id)
      const next: Record<string, ExamAssignmentInput> = {}
      existingAssignments.forEach((assignment) => {
        next[assignment.studentId] = {
          studentId: assignment.studentId,
          isEligible: assignment.isEligible,
          workstationId: assignment.workstationId,
        }
      })
      setAssignments(next)
      setMode('edit')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load exam assignments.')
    } finally {
      setBusy(false)
    }
  }

  function toggleStudent(studentId: string, checked: boolean) {
    setAssignments((current) => {
      const next = { ...current }
      if (!checked) {
        delete next[studentId]
      } else {
        next[studentId] = { studentId, isEligible: true, workstationId: null }
      }
      return next
    })
  }

  function updateStudentAssignment(studentId: string, patch: Partial<ExamAssignmentInput>) {
    setAssignments((current) => {
      const existing = current[studentId]
      if (!existing) return current
      return { ...current, [studentId]: { ...existing, ...patch } }
    })
  }

  async function onSaveExam() {
    if (!name.trim() || !date || !startTime || duration <= 0) {
      setError('Complete exam title, date, start time, and duration.')
      return
    }

    setBusy(true)
    setError(null)
    setNotice(null)

    try {
      const startUtc = toUtcIso(date, startTime)
      const endDate = new Date(`${date}T${startTime}`)
      endDate.setMinutes(endDate.getMinutes() + duration)
      const endUtc = endDate.toISOString()

      const basePayload = {
        name: name.trim(),
        startUtc,
        endUtc,
        labId,
        proctorUserId,
        instructions: instructions.trim() ? instructions.trim() : null,
      }

      if (mode === 'edit' && editingExamId) {
        await updateExam(editingExamId, { ...basePayload, isActive })
        await updateExamAssignments(editingExamId, selectedAssignments)
        setNotice('Exam updated.')
      } else {
        const payload: CreateExamPayload = { ...basePayload, assignments: selectedAssignments }
        await createExam(payload)
        setNotice('Exam created.')
      }

      await loadStaticData()
      resetForm()
      setMode('list')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save exam.')
    } finally {
      setBusy(false)
    }
  }

  if (mode !== 'list') {
    return (
      <AdminLayout>
        <div className="dashboard-content">
          <header className="page-header">
            <div className="header-text">
              <p className="eyebrow">Exam Configuration & Scheduling</p>
              <h1>{mode === 'edit' ? 'Edit Exam' : 'Create New Exam'}</h1>
              <p className="subtext">Set the schedule, assign a teacher proctor, select candidates, and map students to lab machines.</p>
            </div>
            <div className="header-actions" style={{ display: 'flex', gap: '0.75rem' }}>
              <button className="secondary-btn" type="button" onClick={() => { resetForm(); setMode('list') }}>
                Cancel
              </button>
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
                    <input className="input-control" value={name} onChange={(event) => setName(event.target.value)} placeholder="Database Systems Midterm" />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Date</label>
                    <input className="input-control" type="date" value={date} onChange={(event) => setDate(event.target.value)} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Start Time</label>
                    <input className="input-control" type="time" value={startTime} onChange={(event) => setStartTime(event.target.value)} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Duration Minutes</label>
                    <input className="input-control" type="number" min={15} value={duration} onChange={(event) => setDuration(Number(event.target.value))} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Exam State</label>
                    <select className="input-control select-control" value={isActive ? 'active' : 'draft'} onChange={(event) => setIsActive(event.target.value === 'active')}>
                      <option value="active">Active / Scheduled</option>
                      <option value="draft">Draft / Disabled</option>
                    </select>
                  </div>
                  <div className="input-group" style={{ gridColumn: 'span 2' }}>
                    <label className="input-label">Instructions</label>
                    <textarea className="input-control" rows={4} value={instructions} onChange={(event) => setInstructions(event.target.value)} placeholder="Exam rules, allowed tools, and submission instructions..." />
                  </div>
                </div>
              </section>

              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">settings_input_component</span>
                  <h3>Lab & Proctor</h3>
                </div>
                <div className="form-grid-2">
                  <div className="input-group">
                    <label className="input-label">Lab</label>
                    <select className="input-control select-control" value={labId ?? ''} onChange={(event) => setLabId(event.target.value || null)}>
                      <option value="">No lab selected</option>
                      {labs.map((lab) => <option key={lab.id} value={lab.id}>{lab.name} ({lab.registeredTerminals} terminals)</option>)}
                    </select>
                  </div>
                  <div className="input-group">
                    <label className="input-label">Teacher Proctor</label>
                    <select className="input-control select-control" value={proctorUserId ?? ''} onChange={(event) => setProctorUserId(event.target.value || null)}>
                      <option value="">No proctor selected</option>
                      {proctors.map((proctor) => <option key={proctor.id} value={proctor.id}>{proctor.username}</option>)}
                    </select>
                  </div>
                </div>
              </section>

              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">group_add</span>
                  <h3>Student Assignment & Workstations</h3>
                </div>
                <div className="search-bar-v2">
                  <span className="material-symbols-outlined">search</span>
                  <input placeholder="Search students..." value={search} onChange={(event) => setSearch(event.target.value)} />
                </div>
                <div className="student-list" style={{ maxHeight: 520 }}>
                  {filteredCandidates.map((candidate) => {
                    const assignment = assignments[candidate.id]
                    return (
                      <div key={candidate.id} className="student-item" style={{ alignItems: 'stretch', gap: '1rem' }}>
                        <div className="student-item-left" style={{ minWidth: 220 }}>
                          <input type="checkbox" checked={Boolean(assignment)} onChange={(event) => toggleStudent(candidate.id, event.target.checked)} />
                          <div className="student-details">
                            <span className="student-name">{candidate.username}</span>
                            <span className="student-email">{candidate.email}</span>
                          </div>
                        </div>
                        <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                          <span className={`status-pill ${candidate.hasBinding ? 'bound' : 'unbound'}`}>{candidate.hasBinding ? 'Device Bound' : 'Unbound'}</span>
                          {assignment && (
                            <>
                              <select className="input-control select-control" style={{ width: 190 }} value={assignment.isEligible ? 'yes' : 'no'} onChange={(event) => updateStudentAssignment(candidate.id, { isEligible: event.target.value === 'yes' })}>
                                <option value="yes">Eligible</option>
                                <option value="no">Ineligible</option>
                              </select>
                              <select className="input-control select-control" style={{ width: 220 }} value={assignment.workstationId ?? ''} onChange={(event) => updateStudentAssignment(candidate.id, { workstationId: event.target.value || null })} disabled={!labId}>
                                <option value="">No workstation</option>
                                {availableWorkstations.map((machine) => <option key={machine.id} value={machine.id}>{machine.name}{machine.ipAddress ? ` - ${machine.ipAddress}` : ''}</option>)}
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
                    <div
                      className="capacity-fill"
                      style={{ width: selectedLab ? `${Math.min(100, (selectedAssignments.length / Math.max(selectedLab.registeredTerminals, 1)) * 100)}%` : '0%' }}
                    />
                  </div>
                </div>
                <div style={{ display: 'grid', gap: '0.75rem', marginTop: '1rem' }}>
                  <span className="status-pill bound">{selectedAssignments.filter((item) => item.isEligible).length} eligible</span>
                  <span className="status-pill unbound">{selectedAssignments.filter((item) => !item.isEligible).length} ineligible</span>
                  <span className="status-pill active">{selectedAssignments.filter((item) => item.workstationId).length} machines mapped</span>
                </div>
                <div className="alert-card" style={{ marginTop: '1rem' }}>
                  <span className="material-symbols-outlined">info</span>
                  <div className="alert-text">
                    <span className="alert-label">WORKSTATION CONTROL</span>
                    <p className="alert-message">
                      Select a lab to assign physical machines. Machine mapping is saved with each student assignment.
                    </p>
                  </div>
                </div>
              </section>
            </aside>
          </div>
        </div>
      </AdminLayout>
    )
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Exam Configuration & Scheduling</p>
            <h1>Exams & Assessments</h1>
            <p className="subtext">Create exams, assign students, and allocate lab machines for scheduled sessions.</p>
          </div>
          <button className="primary-btn" type="button" onClick={startCreate}>
            <span className="material-symbols-outlined">add</span>
            Create Exam
          </button>
        </header>

        {notice && <div className="inline-alert">{notice}</div>}
        {error && <div className="inline-alert error">{error}</div>}

        <section className="glass-card table-container">
          <div className="table-header-row">
            <h3>Exam Schedule</h3>
            <button className="secondary-btn" type="button" onClick={() => void loadStaticData()}>
              <span className="material-symbols-outlined">refresh</span>
              Refresh
            </button>
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
                {exams.map((exam) => (
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
                        <span className="student-email">{new Date(exam.startUtc).toLocaleTimeString()} - {new Date(exam.endUtc).toLocaleTimeString()}</span>
                      </div>
                    </td>
                    <td>{exam.labName ?? 'Unassigned'}</td>
                    <td>{exam.proctorName ?? 'Unassigned'}</td>
                    <td>{exam.eligibleCount}/{exam.candidateCount}</td>
                    <td><span className={`status-badge ${exam.status === 'Live' ? 'active' : 'secure'}`}>{exam.status}</span></td>
                    <td>
                      <button className="icon-action-btn" type="button" onClick={() => void startEdit(exam)} disabled={busy}>
                        <span className="material-symbols-outlined">edit</span>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {exams.length === 0 && <p className="empty-state">No exams scheduled yet.</p>}
          </div>
        </section>
      </div>
    </AdminLayout>
  )
}
