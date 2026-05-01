import { useEffect, useMemo, useState } from 'react'
import { createExam, getExamCandidates, getExamProctors, getExams } from '../api/exams'
import { getLabs } from '../api/institution'
import { AdminLayout } from '../components/AdminLayout'
import type { CreateExamPayload, ExamCandidate, ExamSummary, Lab, Proctor } from '../types'

export default function Exams() {
  const [isCreating, setIsCreating] = useState(false)
  const [exams, setExams] = useState<ExamSummary[]>([])
  const [candidates, setCandidates] = useState<ExamCandidate[]>([])
  const [labs, setLabs] = useState<Lab[]>([])
  const [proctors, setProctors] = useState<Proctor[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [date, setDate] = useState('')
  const [startTime, setStartTime] = useState('09:00')
  const [duration, setDuration] = useState(120)
  const [labId, setLabId] = useState<string | null>(null)
  const [proctorUserId, setProctorUserId] = useState<string | null>(null)
  const [instructions, setInstructions] = useState('')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Record<string, boolean>>({})

  const filteredCandidates = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return candidates
    return candidates.filter((candidate) =>
      `${candidate.username} ${candidate.email}`.toLowerCase().includes(term),
    )
  }, [candidates, search])

  const selectedAssignments = useMemo(() => {
    return Object.entries(selected)
      .filter(([, isAssigned]) => isAssigned)
      .map(([studentId]) => ({ studentId, isEligible: true }))
  }, [selected])

  useEffect(() => {
    async function loadData() {
      try {
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
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load exam data.')
      }
    }

    void loadData()
  }, [])

  function toUtcIso(dateValue: string, timeValue: string): string {
    const local = new Date(`${dateValue}T${timeValue}`)
    return new Date(local.getTime() - local.getTimezoneOffset() * 60000).toISOString()
  }

  async function onPublishExam() {
    if (!name.trim() || !date || !startTime || duration <= 0) {
      setError('Please complete all exam details before publishing.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const startUtc = toUtcIso(date, startTime)
      const endUtcDate = new Date(`${date}T${startTime}`)
      endUtcDate.setMinutes(endUtcDate.getMinutes() + duration)
      const endUtc = new Date(endUtcDate.getTime() - endUtcDate.getTimezoneOffset() * 60000).toISOString()

      const payload: CreateExamPayload = {
        name: name.trim(),
        startUtc,
        endUtc,
        labId,
        proctorUserId,
        instructions: instructions.trim() ? instructions.trim() : null,
        assignments: selectedAssignments,
      }

      await createExam(payload)
      const refreshed = await getExams()
      setExams(refreshed)
      setIsCreating(false)
      setName('')
      setDate('')
      setStartTime('09:00')
      setDuration(120)
      setLabId(null)
      setProctorUserId(null)
      setInstructions('')
      setSelected({})
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to publish exam.')
    } finally {
      setBusy(false)
    }
  }

  if (isCreating) {
    return (
      <AdminLayout>
        <div className="dashboard-content">
          <header className="page-header">
            <div className="header-text">
              <p className="eyebrow">Exam Management</p>
              <h1>Create New Exam</h1>
              <p className="subtext">Configure details, proctors, and candidate assignments.</p>
            </div>
            <div className="header-actions">
              <button className="secondary-btn" onClick={() => setIsCreating(false)}>
                Cancel
              </button>
              <button className="primary-btn" onClick={() => void onPublishExam()} disabled={busy}>
                {busy ? 'Publishing...' : 'Publish Exam'}
              </button>
            </div>
          </header>

          {error && <div className="inline-alert error">{error}</div>}

          <div className="exam-grid">
            <div className="exam-main">
              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">edit_note</span>
                  <h3>Exam Details</h3>
                </div>
                <div className="form-grid-2">
                  <div className="input-group col-span-2" style={{ gridColumn: 'span 2' }}>
                    <label className="input-label">Exam Title</label>
                    <input
                      className="input-control"
                      placeholder="e.g. Advanced Cybersecurity Finals 2026"
                      value={name}
                      onChange={(event) => setName(event.target.value)}
                      required
                    />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Date</label>
                    <input className="input-control" type="date" value={date} onChange={(event) => setDate(event.target.value)} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Duration (Minutes)</label>
                    <input
                      className="input-control"
                      type="number"
                      min={15}
                      value={duration}
                      onChange={(event) => setDuration(Number(event.target.value))}
                    />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Start Time</label>
                    <input className="input-control" type="time" value={startTime} onChange={(event) => setStartTime(event.target.value)} />
                  </div>
                  <div className="input-group col-span-2" style={{ gridColumn: 'span 2' }}>
                    <label className="input-label">Instructions / Allowed Resources</label>
                    <textarea
                      className="input-control"
                      rows={4}
                      placeholder="Explain tasks, submission rules, and allowed tools..."
                      value={instructions}
                      onChange={(event) => setInstructions(event.target.value)}
                    />
                  </div>
                </div>
              </section>

              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">settings_input_component</span>
                  <h3>Configuration</h3>
                </div>
                <div className="form-grid-2">
                  <div className="input-group">
                    <label className="input-label">Select Lab/Room</label>
                    <select className="select-control" value={labId ?? ''} onChange={(event) => setLabId(event.target.value || null)}>
                      <option value="">Select lab</option>
                      {labs.map((lab) => (
                        <option key={lab.id} value={lab.id}>{lab.name} (Cap: {lab.registeredTerminals})</option>
                      ))}
                    </select>
                  </div>
                  <div className="input-group">
                    <label className="input-label">Assign Teacher Proctor</label>
                    <select className="select-control" value={proctorUserId ?? ''} onChange={(event) => setProctorUserId(event.target.value || null)}>
                      <option value="">Select proctor</option>
                      {proctors.map((proctor) => (
                        <option key={proctor.id} value={proctor.id}>{proctor.username}</option>
                      ))}
                    </select>
                  </div>
                </div>
              </section>

              <section className="glass-card student-assign-card">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">group_add</span>
                  <h3>Student Assignment</h3>
                </div>
                <div className="search-bar-v2">
                  <span className="material-symbols-outlined">search</span>
                  <input placeholder="Search by name or email..." value={search} onChange={(event) => setSearch(event.target.value)} />
                </div>
                <div className="student-list">
                  {filteredCandidates.map((candidate) => (
                    <div key={candidate.id} className="student-item">
                      <div className="student-item-left">
                        <input
                          type="checkbox"
                          className="custom-checkbox"
                          checked={Boolean(selected[candidate.id])}
                          onChange={(event) =>
                            setSelected((prev) => ({ ...prev, [candidate.id]: event.target.checked }))
                          }
                        />
                        <div className="student-details">
                          <span className="student-name">{candidate.username}</span>
                          <span className="student-email">{candidate.email}</span>
                        </div>
                      </div>
                      <span className={`status-pill ${candidate.hasBinding ? 'bound' : 'unbound'}`}>
                        {candidate.hasBinding ? 'Bound' : 'Unbound'}
                      </span>
                    </div>
                  ))}
                  {filteredCandidates.length === 0 && (
                    <div className="empty-state">No students match your search.</div>
                  )}
                </div>
              </section>
            </div>

            <aside className="exam-sidebar">
              <section className="glass-card assignment-summary-card">
                <h3 className="font-h3 mb-6">Assignment Summary</h3>
                <div className="summary-stat">
                  <p className="summary-stat-label">Total Assigned</p>
                  <p className="summary-stat-value">{selectedAssignments.length}</p>
                </div>
                <div className="form-grid-2 mb-6">
                  <div className="bg-surface-alt p-4 rounded-lg">
                    <p className="summary-stat-label" style={{ fontSize: '0.6rem' }}>Eligible</p>
                    <p className="font-bold text-green-600">{selectedAssignments.length}</p>
                  </div>
                  <div className="bg-surface-alt p-4 rounded-lg">
                    <p className="summary-stat-label" style={{ fontSize: '0.6rem' }}>Ineligible</p>
                    <p className="font-bold text-red-600">0</p>
                  </div>
                </div>
                <div className="capacity-info">
                  <div className="capacity-header">
                    <span>Room Capacity Status</span>
                    <span>{labId ? 'Configured' : 'Unassigned'}</span>
                  </div>
                  <div className="capacity-bar">
                    <div className="capacity-fill" style={{ width: labId ? '70%' : '0%' }}></div>
                  </div>
                </div>
                <div className="proctor-preview">
                  <img
                    className="proctor-avatar"
                    src={`https://ui-avatars.com/api/?name=${encodeURIComponent(
                      proctors.find((p) => p.id === proctorUserId)?.username ?? 'Proctor',
                    )}&background=0040a1&color=fff`}
                    alt="Proctor"
                  />
                  <div className="proctor-info">
                    <span className="proctor-name">
                      {proctors.find((p) => p.id === proctorUserId)?.username ?? 'Select a proctor'}
                    </span>
                    <span className="proctor-role">Assigned proctor</span>
                  </div>
                </div>
                <div className="alert-card">
                  <span className="material-symbols-outlined">warning</span>
                  <div className="alert-text">
                    <span className="alert-label">SYSTEM ALERT</span>
                    <p className="alert-message">
                      {selectedAssignments.length === 0
                        ? 'No students assigned yet. Add candidates before publishing.'
                        : 'Review assignment list before publishing.'}
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
            <p className="eyebrow">Exam Control</p>
            <h1>Exams & Assessments</h1>
            <p className="subtext">Manage your scheduled exams and create new assessment sessions.</p>
          </div>
          <div className="header-actions">
            <button className="primary-btn" onClick={() => setIsCreating(true)}>
              <span className="material-symbols-outlined">add</span>
              Create New Exam
            </button>
          </div>
        </header>

        {error && <div className="inline-alert error">{error}</div>}

        <section className="glass-card table-container">
          <div className="table-header-row">
            <h3>Active & Scheduled Exams</h3>
            <div className="table-actions">
              <button className="secondary-btn">
                <span className="material-symbols-outlined">filter_list</span>
                Filter
              </button>
            </div>
          </div>
          <div className="table-wrapper">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Exam Title</th>
                  <th>Date & Time</th>
                  <th>Room / Lab</th>
                  <th>Proctor</th>
                  <th>Candidates</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {exams.map((exam) => (
                  <tr key={exam.id} className="table-row">
                    <td>
                      <span className="font-semibold">{exam.name}</span>
                    </td>
                    <td>
                      <div className="student-details">
                        <span className="student-name" style={{ fontSize: '0.85rem' }}>{new Date(exam.startUtc).toLocaleDateString()}</span>
                        <span className="student-email">{new Date(exam.startUtc).toLocaleTimeString()}</span>
                      </div>
                    </td>
                    <td>{exam.labName ?? 'Unassigned'}</td>
                    <td>{exam.proctorName ?? 'Unassigned'}</td>
                    <td>{exam.candidateCount}</td>
                    <td>
                      <span className={`status-badge ${exam.status === 'Live' ? 'active' : ''}`}>
                        <span className="dot"></span>
                        {exam.status}
                      </span>
                    </td>
                    <td className="action-cell">
                      <button className="icon-action-btn" title="Edit disabled">
                        <span className="material-symbols-outlined">edit</span>
                      </button>
                    </td>
                  </tr>
                ))}
                {exams.length === 0 && (
                  <tr>
                    <td colSpan={7} className="empty-state">No exams scheduled yet.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </AdminLayout>
  )
}
