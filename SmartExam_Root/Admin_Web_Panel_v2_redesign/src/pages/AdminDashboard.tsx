import type { FormEvent } from 'react'
import { useEffect, useMemo, useState } from 'react'
import {
  createStudent,
  createTeacher,
  deleteUser,
  forceStudentLogout,
  getStudentBindings,
  getStudents,
  getTeachers,
  resetStudentBinding,
  toggleUserActive,
  uploadStudentCsv,
} from '../api/admin'
import { getLiveRoster } from '../api/exams'
import { AdminLayout } from '../components/AdminLayout'
import { normalizeRole } from '../roleUtils'
import { useAuth } from '../store/AuthContext'
import type { LiveRosterItem, StudentBindingStatus, UserListItem } from '../types'

type UserTab = 'students' | 'teachers'

function formatSeconds(totalSeconds: number): string {
  const clamped = Math.max(totalSeconds, 0)
  const hours = Math.floor(clamped / 3600)
  const minutes = Math.floor((clamped % 3600) / 60)
  const seconds = clamped % 60
  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
}

function validateUser(username: string, email: string, password: string): string | null {
  if (username.trim().length < 3) return 'Username must be at least 3 characters.'
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return 'Enter a valid email address.'
  if (password.length < 8) return 'Password must be at least 8 characters.'
  return null
}

export function AdminDashboard() {
  const { user } = useAuth()
  const role = normalizeRole(user?.role)
  const canManageUsers = role === 'OrganizationAdmin' || role === 'SuperAdmin'

  const [activeTab, setActiveTab] = useState<UserTab>('students')
  const [students, setStudents] = useState<UserListItem[]>([])
  const [teachers, setTeachers] = useState<UserListItem[]>([])
  const [bindings, setBindings] = useState<StudentBindingStatus[]>([])
  const [roster, setRoster] = useState<LiveRosterItem[]>([])
  const [notice, setNotice] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [csvFile, setCsvFile] = useState<File | null>(null)

  const onlineCount = useMemo(() => roster.filter((x) => x.isOnline).length, [roster])
  const activeStudents = useMemo(() => students.filter((x) => x.isActive).length, [students])
  const activeTeachers = useMemo(() => teachers.filter((x) => x.isActive).length, [teachers])

  async function loadRoster() {
    setRoster(await getLiveRoster())
  }

  async function loadAdminData() {
    if (!canManageUsers) return
    const [teacherData, studentData, bindingData] = await Promise.all([
      getTeachers(),
      getStudents(),
      getStudentBindings(),
    ])
    setTeachers(teacherData)
    setStudents(studentData)
    setBindings(bindingData)
  }

  async function loadAll() {
    setError(null)
    try {
      await Promise.all([loadRoster(), loadAdminData()])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load dashboard data.')
    }
  }

  useEffect(() => {
    void loadAll()
    const timer = window.setInterval(() => void loadRoster().catch(() => undefined), 10000)
    return () => window.clearInterval(timer)
  }, [canManageUsers])

  async function onCreateUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canManageUsers) return

    const validation = validateUser(username, email, password)
    if (validation) {
      setError(validation)
      return
    }

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      if (activeTab === 'teachers') {
        await createTeacher({ username: username.trim(), email: email.trim(), password })
        setNotice('Teacher account created.')
      } else {
        await createStudent({ username: username.trim(), email: email.trim(), password })
        setNotice('Student account created.')
      }
      setUsername('')
      setEmail('')
      setPassword('')
      await loadAdminData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create user.')
    } finally {
      setBusy(false)
    }
  }

  async function onUploadCsv(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canManageUsers || !csvFile) return

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const result = await uploadStudentCsv(csvFile)
      setCsvFile(null)
      await loadAdminData()
      setNotice(`CSV imported. Created ${result.createdCount}, skipped ${result.skippedCount}.`)
      if (result.errors.length > 0) setError(result.errors.slice(0, 3).join(' '))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'CSV upload failed.')
    } finally {
      setBusy(false)
    }
  }

  async function onToggleUser(userId: string) {
    setBusy(true)
    setError(null)
    try {
      await toggleUserActive(userId)
      await loadAdminData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update user.')
    } finally {
      setBusy(false)
    }
  }

  async function onDeleteUser(userId: string) {
    if (!window.confirm('Delete this user account?')) return
    setBusy(true)
    setError(null)
    try {
      await deleteUser(userId)
      await loadAdminData()
      setNotice('User deleted.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete user.')
    } finally {
      setBusy(false)
    }
  }

  async function onResetBinding(studentId: string) {
    setBusy(true)
    setError(null)
    try {
      await resetStudentBinding(studentId)
      await loadAdminData()
      setNotice('Device binding reset.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to reset binding.')
    } finally {
      setBusy(false)
    }
  }

  async function onForceLogout(studentId: string) {
    setBusy(true)
    setError(null)
    try {
      await forceStudentLogout(studentId)
      await loadRoster()
      setNotice('Active student sessions terminated.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to force logout.')
    } finally {
      setBusy(false)
    }
  }

  const currentUsers = activeTab === 'teachers' ? teachers : students

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <div className="page-header">
          <div className="header-text">
            <p className="eyebrow">{canManageUsers ? 'User Authentication & Role Management' : 'Teacher Dashboard'}</p>
            <h1>{canManageUsers ? 'Users & Live Sessions' : 'Live Exam Overview'}</h1>
            <p className="subtext">
              {canManageUsers
                ? 'Create teachers and students, control account access, and handle student device bindings.'
                : 'Monitor assigned students and exam session presence.'}
            </p>
          </div>
          <button className="secondary-btn" type="button" onClick={() => void loadAll()}>
            <span className="material-symbols-outlined">refresh</span>
            Refresh
          </button>
        </div>

        {notice && <div className="inline-alert">{notice}</div>}
        {error && <div className="inline-alert error">{error}</div>}

        <div className="stats-grid">
          <div className="card stat-card">
            <div className="stat-icon blue"><span className="material-symbols-outlined">groups</span></div>
            <div><span className="stat-value">{students.length}</span><span className="stat-label">Students</span></div>
          </div>
          <div className="card stat-card">
            <div className="stat-icon green"><span className="material-symbols-outlined">school</span></div>
            <div><span className="stat-value">{teachers.length}</span><span className="stat-label">Teachers</span></div>
          </div>
          <div className="card stat-card">
            <div className="stat-icon purple"><span className="material-symbols-outlined">wifi_tethering</span></div>
            <div><span className="stat-value">{onlineCount}</span><span className="stat-label">Students Online</span></div>
          </div>
          <div className="card stat-card">
            <div className="stat-icon red"><span className="material-symbols-outlined">verified_user</span></div>
            <div><span className="stat-value">{activeStudents + activeTeachers}</span><span className="stat-label">Active Accounts</span></div>
          </div>
        </div>

        {canManageUsers && (
          <div className="dashboard-main-grid">
            <section className="glass-card form-section">
              <div className="section-title-row">
                <span className="material-symbols-outlined">person_add</span>
                <h3>Create Account</h3>
              </div>
              <div style={{ display: 'flex', gap: '0.75rem', marginBottom: '1rem' }}>
                <button className={activeTab === 'students' ? 'primary-btn' : 'secondary-btn'} type="button" onClick={() => setActiveTab('students')}>
                  Students
                </button>
                <button className={activeTab === 'teachers' ? 'primary-btn' : 'secondary-btn'} type="button" onClick={() => setActiveTab('teachers')}>
                  Teachers
                </button>
              </div>
              <form onSubmit={onCreateUser}>
                <div className="form-grid-2">
                  <div className="input-group">
                    <label className="input-label">Username</label>
                    <input className="input-control" value={username} onChange={(e) => setUsername(e.target.value)} placeholder={activeTab === 'students' ? 'student01' : 'teacher01'} />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Email</label>
                    <input className="input-control" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="name@institution.edu" />
                  </div>
                </div>
                <div className="input-group">
                  <label className="input-label">Password</label>
                  <input className="input-control" type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Minimum 8 characters" />
                </div>
                <button className="primary-btn" type="submit" disabled={busy}>
                  <span className="material-symbols-outlined">add</span>
                  Create {activeTab === 'students' ? 'Student' : 'Teacher'}
                </button>
              </form>

              {activeTab === 'students' && (
                <form onSubmit={onUploadCsv} style={{ marginTop: '1.5rem' }}>
                  <div className="input-group">
                    <label className="input-label">Batch Upload Students CSV</label>
                    <input className="input-control" type="file" accept=".csv" onChange={(e) => setCsvFile(e.target.files?.[0] ?? null)} />
                  </div>
                  <button className="secondary-btn" type="submit" disabled={busy || !csvFile}>
                    <span className="material-symbols-outlined">upload_file</span>
                    Upload CSV
                  </button>
                </form>
              )}
            </section>

            <section className="glass-card roster-card">
              <div className="card-header">
                <h3>Live Roster</h3>
                <span className="live-indicator"><span className="pulse"></span> Live</span>
              </div>
              <div className="table-wrapper">
                <table className="admin-table">
                  <thead>
                    <tr><th>Student</th><th>Status</th><th>Remaining</th><th>Connection</th></tr>
                  </thead>
                  <tbody>
                    {roster.slice(0, 8).map((item) => (
                      <tr key={item.studentId}>
                        <td>{item.username}</td>
                        <td>{item.status}</td>
                        <td>{formatSeconds(item.remainingSeconds)}</td>
                        <td><span className={`connection-pill ${item.isOnline ? 'online' : 'offline'}`}>{item.isOnline ? 'Online' : 'Offline'}</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {roster.length === 0 && <p className="empty-state">No student sessions yet.</p>}
              </div>
            </section>
          </div>
        )}

        {canManageUsers && (
          <section className="glass-card table-container">
            <div className="table-header-row">
              <h3>{activeTab === 'students' ? 'Student Accounts' : 'Teacher Accounts'}</h3>
              <span className="status-badge secure">{currentUsers.length} records</span>
            </div>
            <div className="table-wrapper">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Email</th>
                    <th>Role</th>
                    <th>Status</th>
                    {activeTab === 'students' && <th>Device</th>}
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {currentUsers.map((account) => {
                    const binding = bindings.find((x) => x.studentId === account.id)
                    return (
                      <tr key={account.id}>
                        <td>{account.username}</td>
                        <td>{account.email}</td>
                        <td>{account.role}</td>
                        <td><span className={`status-pill ${account.isActive ? 'active' : 'unbound'}`}>{account.isActive ? 'Active' : 'Inactive'}</span></td>
                        {activeTab === 'students' && (
                          <td><span className={`status-pill ${binding?.hasBinding ? 'bound' : 'unbound'}`}>{binding?.hasBinding ? 'Bound' : 'Unbound'}</span></td>
                        )}
                        <td>
                          <button className="icon-action-btn" type="button" onClick={() => onToggleUser(account.id)} disabled={busy}>
                            <span className="material-symbols-outlined">{account.isActive ? 'block' : 'check_circle'}</span>
                          </button>
                          {activeTab === 'students' && (
                            <>
                              <button className="icon-action-btn" type="button" onClick={() => onResetBinding(account.id)} disabled={busy}>
                                <span className="material-symbols-outlined">devices_off</span>
                              </button>
                              <button className="icon-action-btn" type="button" onClick={() => onForceLogout(account.id)} disabled={busy}>
                                <span className="material-symbols-outlined">logout</span>
                              </button>
                            </>
                          )}
                          <button className="icon-action-btn danger" type="button" onClick={() => onDeleteUser(account.id)} disabled={busy}>
                            <span className="material-symbols-outlined">delete</span>
                          </button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
              {currentUsers.length === 0 && <p className="empty-state">No {activeTab} created yet.</p>}
            </div>
          </section>
        )}

        {!canManageUsers && (
          <section className="glass-card table-container">
            <div className="table-header-row">
              <h3>Live Student Roster</h3>
              <span className="status-badge secure">{onlineCount} online</span>
            </div>
            <div className="table-wrapper">
              <table className="admin-table">
                <thead><tr><th>Student</th><th>Status</th><th>Remaining</th><th>Last Heartbeat</th><th>Connection</th></tr></thead>
                <tbody>
                  {roster.map((item) => (
                    <tr key={item.studentId}>
                      <td>{item.username}</td>
                      <td>{item.status}</td>
                      <td>{formatSeconds(item.remainingSeconds)}</td>
                      <td>{item.lastHeartbeatUtc ? new Date(item.lastHeartbeatUtc).toLocaleString() : 'Never'}</td>
                      <td><span className={`connection-pill ${item.isOnline ? 'online' : 'offline'}`}>{item.isOnline ? 'Online' : 'Offline'}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {roster.length === 0 && <p className="empty-state">No active or historical student sessions yet.</p>}
            </div>
          </section>
        )}
      </div>
    </AdminLayout>
  )
}
