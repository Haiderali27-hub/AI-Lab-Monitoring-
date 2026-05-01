import type { FormEvent } from 'react'
import { useEffect, useMemo, useState } from 'react'
import { createStudent, createTeacher, forceStudentLogout, getStudentBindings, resetStudentBinding, uploadStudentCsv } from '../api/admin'
import { getLiveRoster } from '../api/exams'
import { AdminLayout } from '../components/AdminLayout'
import { UserTable } from '../components/UserTable'
import { useAuth } from '../store/AuthContext'
import type { LiveRosterItem, StudentBindingStatus } from '../types'

function formatSeconds(totalSeconds: number): string {
  const clamped = Math.max(totalSeconds, 0)
  const hours = Math.floor(clamped / 3600)
  const minutes = Math.floor((clamped % 3600) / 60)
  const seconds = clamped % 60
  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds
    .toString()
    .padStart(2, '0')}`
}

export function AdminDashboard() {
  const { user } = useAuth()

  const [bindings, setBindings] = useState<StudentBindingStatus[]>([])
  const [roster, setRoster] = useState<LiveRosterItem[]>([])
  const [message, setMessage] = useState('System active and monitoring.')
  const [busy, setBusy] = useState(false)

  const [teacherUsername, setTeacherUsername] = useState('')
  const [teacherEmail, setTeacherEmail] = useState('')
  const [teacherPassword, setTeacherPassword] = useState('')

  const [studentUsername, setStudentUsername] = useState('')
  const [studentEmail, setStudentEmail] = useState('')
  const [studentPassword, setStudentPassword] = useState('')
  const [csvFile, setCsvFile] = useState<File | null>(null)

  const onlineCount = useMemo(() => roster.filter((x) => x.isOnline).length, [roster])

  async function loadBindings() {
    const result = await getStudentBindings()
    setBindings(result)
  }

  async function loadRoster() {
    const result = await getLiveRoster()
    setRoster(result)
  }

  async function refreshAll() {
    try {
      await Promise.all([loadBindings(), loadRoster()])
      setMessage('Dashboard data refreshed.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to refresh dashboard data.')
    }
  }

  useEffect(() => {
    void refreshAll()
    const intervalId = window.setInterval(() => {
      void loadRoster()
    }, 15000)

    return () => {
      window.clearInterval(intervalId)
    }
  }, [])

  async function onCreateTeacher(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      await createTeacher({ username: teacherUsername, email: teacherEmail, password: teacherPassword })
      setTeacherUsername('')
      setTeacherEmail('')
      setTeacherPassword('')
      setMessage('Teacher account created successfully.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to create teacher.')
    } finally {
      setBusy(false)
    }
  }

  async function onCreateStudent(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      await createStudent({ username: studentUsername, email: studentEmail, password: studentPassword })
      setStudentUsername('')
      setStudentEmail('')
      setStudentPassword('')
      await loadBindings()
      setMessage('Student account created successfully.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to create student.')
    } finally {
      setBusy(false)
    }
  }

  async function onUploadCsv(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!csvFile) {
      setMessage('Please select a CSV file first.')
      return
    }

    setBusy(true)
    try {
      const result = await uploadStudentCsv(csvFile)
      await loadBindings()
      setCsvFile(null)
      setMessage(`Batch upload complete: ${result.createdCount} created, ${result.skippedCount} skipped.`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'CSV upload failed.')
    } finally {
      setBusy(false)
    }
  }

  async function onResetBinding(studentId: string) {
    setBusy(true)
    try {
      await resetStudentBinding(studentId)
      await loadBindings()
      setMessage('Student device binding has been reset.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to reset binding.')
    } finally {
      setBusy(false)
    }
  }

  async function onForceLogout(studentId: string) {
    setBusy(true)
    try {
      await forceStudentLogout(studentId)
      setMessage('All active sessions for student have been terminated.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Failed to terminate session.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Control Center Dashboard</p>
            <h1>Welcome back, {user?.username}</h1>
            <p className="subtext">Overview of current lab activity and user management.</p>
          </div>
          <div className="header-actions">
            <button className="refresh-btn" onClick={() => void refreshAll()}>
              <span className="material-symbols-outlined">sync</span>
              Refresh Live Data
            </button>
          </div>
        </header>

        <section className="stats-grid">
          <div className="card stat-card">
            <div className="stat-icon blue">
              <span className="material-symbols-outlined icon-fill">group</span>
            </div>
            <div className="stat-info">
              <span className="stat-label">Total Students</span>
              <span className="stat-value">{bindings.length}</span>
            </div>
          </div>
          <div className="card stat-card">
            <div className="stat-icon green">
              <span className="material-symbols-outlined icon-fill">devices</span>
            </div>
            <div className="stat-info">
              <span className="stat-label">Device Bound</span>
              <span className="stat-value">{bindings.filter((x) => x.hasBinding).length}</span>
            </div>
          </div>
          <div className="card stat-card">
            <div className="stat-icon purple">
              <span className="material-symbols-outlined icon-fill">bolt</span>
            </div>
            <div className="stat-info">
              <span className="stat-label">Active Now</span>
              <span className="stat-value">{onlineCount}</span>
            </div>
          </div>
          <div className="card stat-card">
            <div className="stat-icon red">
              <span className="material-symbols-outlined icon-fill">report_problem</span>
            </div>
            <div className="stat-info">
              <span className="stat-label">Security Alerts</span>
              <span className="stat-value">0</span>
            </div>
          </div>
        </section>

        <div className="dashboard-main-grid">
          <div className="grid-left">
            <UserTable
              students={bindings}
              onResetBinding={onResetBinding}
              onForceLogout={onForceLogout}
            />

            <section className="card roster-card">
              <div className="card-header">
                <h3>Live Exam Roster</h3>
                <span className="live-indicator">
                  <span className="pulse"></span>
                  LIVE
                </span>
              </div>
              <div className="roster-table-wrapper">
                <table className="admin-table roster-table">
                  <thead>
                    <tr>
                      <th>Student</th>
                      <th>Status</th>
                      <th>Time Remaining</th>
                      <th>Connection</th>
                    </tr>
                  </thead>
                  <tbody>
                    {roster.map((item) => (
                      <tr key={item.studentId} className="table-row">
                        <td>
                          <span className="font-semibold">{item.username}</span>
                        </td>
                        <td>
                          <span className={`status-badge ${item.status === 'Taking' ? 'active' : ''}`}>
                            {item.status}
                          </span>
                        </td>
                        <td className="font-mono">{formatSeconds(item.remainingSeconds)}</td>
                        <td>
                          <span className={`connection-pill ${item.isOnline ? 'online' : 'offline'}`}>
                            {item.isOnline ? 'Stable' : 'Disconnected'}
                          </span>
                        </td>
                      </tr>
                    ))}
                    {roster.length === 0 && (
                      <tr>
                        <td colSpan={4} className="empty-state">
                          No active exam sessions.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </section>
          </div>

          <aside className="grid-right">
            <section className="card quick-action-card">
              <h3>Teacher Onboarding</h3>
              <form className="quick-form" onSubmit={onCreateTeacher}>
                <div className="form-group">
                  <label>Username</label>
                  <input
                    value={teacherUsername}
                    onChange={(e) => setTeacherUsername(e.target.value)}
                    placeholder="e.g. j.doe"
                    required
                  />
                </div>
                <div className="form-group">
                  <label>Email</label>
                  <input
                    type="email"
                    value={teacherEmail}
                    onChange={(e) => setTeacherEmail(e.target.value)}
                    placeholder="teacher@institution.edu"
                    required
                  />
                </div>
                <div className="form-group">
                  <label>Initial Password</label>
                  <input
                    type="password"
                    value={teacherPassword}
                    onChange={(e) => setTeacherPassword(e.target.value)}
                    placeholder="••••••••"
                    required
                  />
                </div>
                <button className="primary-btn full-width" type="submit" disabled={busy}>
                  Create Staff Account
                </button>
              </form>
            </section>

            <section className="card quick-action-card">
              <h3>Batch Registration</h3>
              <p className="card-subtext">Upload a CSV file with student details.</p>
              <form className="quick-form" onSubmit={onUploadCsv}>
                <div className="file-upload-zone">
                  <span className="material-symbols-outlined">upload_file</span>
                  <input
                    type="file"
                    accept=".csv"
                    onChange={(e) => setCsvFile(e.target.files?.[0] ?? null)}
                  />
                  <p>{csvFile ? csvFile.name : 'Click or drag CSV here'}</p>
                </div>
                <button className="secondary-btn full-width" type="submit" disabled={busy}>
                  Upload Roster
                </button>
              </form>
            </section>

            <div className="status-footer-card">
              <span className="material-symbols-outlined">info</span>
              <p>{message}</p>
            </div>
          </aside>
        </div>
      </div>
    </AdminLayout>
  )
}