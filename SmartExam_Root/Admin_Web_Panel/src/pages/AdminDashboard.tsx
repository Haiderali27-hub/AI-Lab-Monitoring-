import type { FormEvent } from 'react'
import { useEffect, useMemo, useState } from 'react'
import {
  createDepartment,
  createSection,
  createStudent,
  createTeacher,
  deleteUser,
  forceStudentLogout,
  getDepartments,
  getSections,
  getStudentBindings,
  getStudents,
  getTeachers,
  resetStudentBinding,
  resetUserPassword,
  toggleUserActive,
  updateUser,
  uploadStudentCsv,
} from '../api/admin'
import { getLiveRoster } from '../api/exams'
import { AdminLayout } from '../components/AdminLayout'
import { SkeletonStatCard, SkeletonTableRow } from '../components/Skeleton'
import { Toaster, useToast } from '../components/Toaster'
import { formatExamSessionStatus, formatRole, normalizeRole } from '../roleUtils'
import { useAuth } from '../store/AuthContext'
import type { AcademicSection, Department, LiveRosterItem, StudentBindingStatus, UserListItem } from '../types'

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
  const { toasts, push: toast, dismiss } = useToast()

  const [loading, setLoading] = useState(true)
  const [activeTab, setActiveTab] = useState<UserTab>('students')
  const [students, setStudents] = useState<UserListItem[]>([])
  const [teachers, setTeachers] = useState<UserListItem[]>([])
  const [departments, setDepartments] = useState<Department[]>([])
  const [sections, setSections] = useState<AcademicSection[]>([])
  const [bindings, setBindings] = useState<StudentBindingStatus[]>([])
  const [roster, setRoster] = useState<LiveRosterItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // User table search
  const [userSearch, setUserSearch] = useState('')
  const [userPage, setUserPage] = useState(1)
  const PAGE_SIZE = 10

  // Edit user modal
  const [editingUser, setEditingUser] = useState<UserListItem | null>(null)
  const [editUsername, setEditUsername] = useState('')
  const [editEmail, setEditEmail] = useState('')
  const [editIsActive, setEditIsActive] = useState(true)
  const [editRole, setEditRole] = useState<'Student' | 'Teacher'>('Student')
  const [editNewPassword, setEditNewPassword] = useState('')
  const [editPasswordVisible, setEditPasswordVisible] = useState(false)
  const [showPasswordReset, setShowPasswordReset] = useState(false)

  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [selectedDepartmentId, setSelectedDepartmentId] = useState('')
  const [selectedSectionId, setSelectedSectionId] = useState('')
  const [csvFile, setCsvFile] = useState<File | null>(null)
  const [departmentName, setDepartmentName] = useState('')
  const [departmentCode, setDepartmentCode] = useState('')
  const [sectionName, setSectionName] = useState('')
  const [sectionCode, setSectionCode] = useState('')
  const [sectionYear, setSectionYear] = useState(new Date().getFullYear().toString())
  const [sectionSemester, setSectionSemester] = useState('')

  const onlineCount = useMemo(() => roster.filter((x) => x.isOnline).length, [roster])
  const activeStudents = useMemo(() => students.filter((x) => x.isActive).length, [students])
  const activeTeachers = useMemo(() => teachers.filter((x) => x.isActive).length, [teachers])
  const availableSections = useMemo(
    () => sections.filter((section) => !selectedDepartmentId || section.departmentId === selectedDepartmentId),
    [sections, selectedDepartmentId],
  )

  const filteredUsers = useMemo(() => {
    const list = activeTab === 'teachers' ? teachers : students
    const term = userSearch.trim().toLowerCase()
    return term ? list.filter((u) => `${u.username} ${u.email}`.toLowerCase().includes(term)) : list
  }, [activeTab, teachers, students, userSearch])

  const pagedUsers = useMemo(() => {
    const start = (userPage - 1) * PAGE_SIZE
    return filteredUsers.slice(start, start + PAGE_SIZE)
  }, [filteredUsers, userPage])

  const totalPages = Math.ceil(filteredUsers.length / PAGE_SIZE)

  async function loadRoster() {
    setRoster(await getLiveRoster())
  }

  async function loadAdminData() {
    if (!canManageUsers) return
    const [teacherData, studentData, bindingData, departmentData, sectionData] = await Promise.all([
      getTeachers(),
      getStudents(),
      getStudentBindings(),
      getDepartments(),
      getSections(),
    ])
    setTeachers(teacherData)
    setStudents(studentData)
    setBindings(bindingData)
    setDepartments(departmentData)
    setSections(sectionData)
  }

  async function loadAll() {
    setLoading(true)
    setError(null)
    try {
      await Promise.all([loadRoster(), loadAdminData()])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load dashboard data.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadAll()
    const timer = window.setInterval(() => void loadRoster().catch(() => undefined), 10000)
    return () => window.clearInterval(timer)
  }, [canManageUsers])

  useEffect(() => {
    setUserPage(1)
  }, [userSearch, activeTab])

  useEffect(() => {
    if (selectedSectionId && !availableSections.some((section) => section.id === selectedSectionId)) {
      setSelectedSectionId('')
    }
  }, [availableSections, selectedSectionId])

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
        await createTeacher({
          username: username.trim(),
          email: email.trim(),
          password,
          departmentId: selectedDepartmentId || null,
          sectionId: selectedSectionId || null,
        })
        toast('Teacher account created.')
      } else {
        await createStudent({
          username: username.trim(),
          email: email.trim(),
          password,
          departmentId: selectedDepartmentId || null,
          sectionId: selectedSectionId || null,
        })
        toast('Student account created.')
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

  async function onCreateDepartment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canManageUsers) return
    if (departmentName.trim().length < 2 || departmentCode.trim().length < 2) {
      setError('Department name and code are required.')
      return
    }

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const department = await createDepartment({
        name: departmentName.trim(),
        code: departmentCode.trim(),
      })
      setDepartmentName('')
      setDepartmentCode('')
      await loadAdminData()
      setSelectedDepartmentId(department.id)
      toast('Department created.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create department.')
    } finally {
      setBusy(false)
    }
  }

  async function onCreateSection(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canManageUsers) return
    if (!selectedDepartmentId) {
      setError('Select a department before creating a section.')
      return
    }
    if (sectionName.trim().length < 2 || sectionCode.trim().length < 2 || sectionYear.trim().length < 4) {
      setError('Section name, code, and academic year are required.')
      return
    }

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const section = await createSection({
        departmentId: selectedDepartmentId,
        name: sectionName.trim(),
        code: sectionCode.trim(),
        academicYear: sectionYear.trim(),
        semester: sectionSemester.trim() || null,
      })
      setSectionName('')
      setSectionCode('')
      setSectionSemester('')
      await loadAdminData()
      setSelectedSectionId(section.id)
      toast('Section created.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create section.')
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
      toast(`CSV imported. Created ${result.createdCount}, skipped ${result.skippedCount}.`)
      if (result.errors.length > 0) toast(result.errors.slice(0, 3).join(' '), 'error')
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
      toast('User deleted.', 'warning')
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
      toast('Device binding reset.', 'warning')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to reset binding.')
    } finally {
      setBusy(false)
    }
  }

  async function onForceLogout(studentId: string) {
    setBusy(true); setError(null)
    try {
      await forceStudentLogout(studentId)
      await loadRoster()
      toast('Active student sessions terminated.', 'warning')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to force logout.')
    } finally { setBusy(false) }
  }

  function openEditUser(account: UserListItem) {
    setEditingUser(account)
    setEditUsername(account.username)
    setEditEmail(account.email)
    setEditIsActive(account.isActive)
    setEditRole(account.role === 'Teacher' ? 'Teacher' : 'Student')
    setEditNewPassword('')
    setShowPasswordReset(false)
    setEditPasswordVisible(false)
    setError(null)
  }

  async function onUpdateUser() {
    if (!editingUser) return
    if (editUsername.trim().length < 3) { setError('Username must be at least 3 characters.'); return }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(editEmail)) { setError('Enter a valid email address.'); return }
    setBusy(true); setError(null)
    try {
      const roleChanged = editRole !== (editingUser.role === 'Teacher' ? 'Teacher' : 'Student')
      await updateUser(editingUser.id, {
        username: editUsername.trim(),
        email: editEmail.trim(),
        isActive: editIsActive,
        ...(roleChanged ? { newRole: editRole } : {}),
      })
      setEditingUser(null)
      await loadAdminData()
      toast(`User "${editUsername.trim()}" updated${roleChanged ? ` — role changed to ${editRole}` : ''}.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update user.')
    } finally { setBusy(false) }
  }

  async function onResetPassword() {
    if (!editingUser) return
    if (editNewPassword.length < 8) { setError('New password must be at least 8 characters.'); return }
    setBusy(true); setError(null)
    try {
      await resetUserPassword(editingUser.id, editNewPassword)
      setEditNewPassword('')
      setShowPasswordReset(false)
      toast(`Password reset for "${editingUser.username}". Active sessions revoked.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to reset password.')
    } finally { setBusy(false) }
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

        {error && <div className="inline-alert error">{error}</div>}

        <div className="stats-grid">
          {loading ? (
            <>
              <SkeletonStatCard />
              <SkeletonStatCard />
              <SkeletonStatCard />
              <SkeletonStatCard />
            </>
          ) : (
            <>
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
              <form onSubmit={onCreateDepartment} className="form-grid-2" style={{ marginBottom: '1rem' }}>
                <div className="input-group">
                  <label className="input-label">Department Name</label>
                  <input className="input-control" value={departmentName} onChange={(e) => setDepartmentName(e.target.value)} placeholder="Computer Science" />
                </div>
                <div className="input-group">
                  <label className="input-label">Department Code</label>
                  <input className="input-control" value={departmentCode} onChange={(e) => setDepartmentCode(e.target.value)} placeholder="CS" />
                </div>
                <button className="secondary-btn" type="submit" disabled={busy} style={{ width: 'max-content' }}>
                  <span className="material-symbols-outlined">domain_add</span>
                  Add Department
                </button>
              </form>
              <form onSubmit={onCreateSection} className="form-grid-2" style={{ marginBottom: '1.25rem' }}>
                <div className="input-group">
                  <label className="input-label">Section Department</label>
                  <select className="input-control select-control" value={selectedDepartmentId} onChange={(e) => setSelectedDepartmentId(e.target.value)}>
                    <option value="">Select department</option>
                    {departments.map((department) => (
                      <option key={department.id} value={department.id}>{department.name} ({department.code})</option>
                    ))}
                  </select>
                </div>
                <div className="input-group">
                  <label className="input-label">Section Name</label>
                  <input className="input-control" value={sectionName} onChange={(e) => setSectionName(e.target.value)} placeholder="BSCS 2026 - A" />
                </div>
                <div className="input-group">
                  <label className="input-label">Section Code</label>
                  <input className="input-control" value={sectionCode} onChange={(e) => setSectionCode(e.target.value)} placeholder="BSCS26A" />
                </div>
                <div className="input-group">
                  <label className="input-label">Academic Year</label>
                  <input className="input-control" value={sectionYear} onChange={(e) => setSectionYear(e.target.value)} placeholder="2026" />
                </div>
                <div className="input-group">
                  <label className="input-label">Semester</label>
                  <input className="input-control" value={sectionSemester} onChange={(e) => setSectionSemester(e.target.value)} placeholder="Spring" />
                </div>
                <button className="secondary-btn" type="submit" disabled={busy || !selectedDepartmentId} style={{ width: 'max-content' }}>
                  <span className="material-symbols-outlined">splitscreen_add</span>
                  Add Section
                </button>
              </form>
              <form onSubmit={onCreateUser}>
                <div className="form-grid-2">
                  <div className="input-group">
                    <label className="input-label">Assign Department</label>
                    <select className="input-control select-control" value={selectedDepartmentId} onChange={(e) => setSelectedDepartmentId(e.target.value)}>
                      <option value="">No department</option>
                      {departments.map((department) => (
                        <option key={department.id} value={department.id}>{department.name} ({department.code})</option>
                      ))}
                    </select>
                  </div>
                  {(activeTab === 'students' || activeTab === 'teachers') && (
                    <div className="input-group">
                      <label className="input-label">{activeTab === 'students' ? 'Assign Section' : 'Teaching Section'}</label>
                      <select className="input-control select-control" value={selectedSectionId} onChange={(e) => setSelectedSectionId(e.target.value)} disabled={!selectedDepartmentId}>
                        <option value="">No section</option>
                        {availableSections.map((section) => (
                          <option key={section.id} value={section.id}>{section.name} ({section.code})</option>
                        ))}
                      </select>
                    </div>
                  )}
                </div>
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
                        <td>{formatExamSessionStatus(item.status)}</td>
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
              <span className="status-badge secure">{filteredUsers.length} records</span>
            </div>
            <div className="search-bar-v2" style={{ marginBottom: '1rem' }}>
              <span className="material-symbols-outlined">search</span>
              <input placeholder={`Search ${activeTab}...`} value={userSearch} onChange={(e) => setUserSearch(e.target.value)} />
            </div>
            <div className="table-wrapper">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Email</th>
                    <th>Role</th>
                    <th>Department</th>
                    <th>Section</th>
                    <th>Status</th>
                    {activeTab === 'students' && <th>Device</th>}
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {pagedUsers.map((account) => {
                    const binding = bindings.find((x) => x.studentId === account.id)
                    return (
                      <tr key={account.id}>
                        <td>{account.username}</td>
                        <td>{account.email}</td>
                        <td>{formatRole(account.role)}</td>
                        <td>{account.departmentName ?? 'Unassigned'}</td>
                        <td>{account.sectionName ?? 'Unassigned'}</td>
                        <td><span className={`status-pill ${account.isActive ? 'active' : 'unbound'}`}>{account.isActive ? 'Active' : 'Inactive'}</span></td>
                        {activeTab === 'students' && (
                          <td><span className={`status-pill ${binding?.hasBinding ? 'bound' : 'unbound'}`}>{binding?.hasBinding ? 'Bound' : 'Unbound'}</span></td>
                        )}
                        <td>
                          <button className="icon-action-btn" type="button" title="Edit user" onClick={() => openEditUser(account)} disabled={busy}>
                            <span className="material-symbols-outlined">edit</span>
                          </button>
                          <button className="icon-action-btn" type="button" onClick={() => void onToggleUser(account.id)} disabled={busy}>
                            <span className="material-symbols-outlined">{account.isActive ? 'block' : 'check_circle'}</span>
                          </button>
                          {activeTab === 'students' && (
                            <>
                              <button className="icon-action-btn" type="button" onClick={() => void onResetBinding(account.id)} disabled={busy}>
                                <span className="material-symbols-outlined">devices_off</span>
                              </button>
                              <button className="icon-action-btn" type="button" onClick={() => void onForceLogout(account.id)} disabled={busy}>
                                <span className="material-symbols-outlined">logout</span>
                              </button>
                            </>
                          )}
                          <button className="icon-action-btn danger" type="button" onClick={() => void onDeleteUser(account.id)} disabled={busy}>
                            <span className="material-symbols-outlined">delete</span>
                          </button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
              {filteredUsers.length === 0 && <p className="empty-state">No {activeTab} match your search.</p>}
            </div>
            {totalPages > 1 && (
              <div style={{ display: 'flex', gap: '0.5rem', justifyContent: 'center', paddingTop: '1rem' }}>
                <button className="secondary-btn" type="button" disabled={userPage === 1} onClick={() => setUserPage((p) => p - 1)}>
                  <span className="material-symbols-outlined">chevron_left</span>
                </button>
                <span style={{ alignSelf: 'center', fontSize: '0.85rem' }}>{userPage} / {totalPages}</span>
                <button className="secondary-btn" type="button" disabled={userPage === totalPages} onClick={() => setUserPage((p) => p + 1)}>
                  <span className="material-symbols-outlined">chevron_right</span>
                </button>
              </div>
            )}
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
                      <td>{formatExamSessionStatus(item.status)}</td>
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

        {/* ── Edit User Modal ─────────────────────────────── */}
        {editingUser && (
          <div style={{
            position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)', zIndex: 999,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}>
            <div className="glass-card" style={{ width: 480, padding: '2rem', position: 'relative', maxHeight: '90vh', overflowY: 'auto' }}>
              <div className="section-title-row" style={{ marginBottom: '1.25rem' }}>
                <span className="material-symbols-outlined">manage_accounts</span>
                <h3>Edit User — {editingUser.username}</h3>
              </div>
              {error && <div className="inline-alert error" style={{ marginBottom: '1rem' }}>{error}</div>}

              {/* Role change warning */}
              {editRole !== (editingUser.role === 'Teacher' ? 'Teacher' : 'Student') && (
                <div className="inline-alert" style={{ marginBottom: '1rem', borderLeft: '3px solid var(--color-warning, #f59e0b)' }}>
                  ⚠️ Changing role from <strong>{editingUser.role}</strong> to <strong>{editRole}</strong> will remove all associated section {editingUser.role === 'Teacher' ? 'assignments' : 'enrollments and exam slots'}.
                </div>
              )}

              {/* Core fields */}
              <div className="form-grid-2" style={{ marginBottom: '1rem' }}>
                <div className="input-group">
                  <label className="input-label">Username</label>
                  <input className="input-control" value={editUsername} onChange={(e) => setEditUsername(e.target.value)} />
                </div>
                <div className="input-group">
                  <label className="input-label">Email</label>
                  <input className="input-control" type="email" value={editEmail} onChange={(e) => setEditEmail(e.target.value)} />
                </div>
                <div className="input-group">
                  <label className="input-label">Status</label>
                  <select className="input-control select-control" value={editIsActive ? 'active' : 'inactive'} onChange={(e) => setEditIsActive(e.target.value === 'active')}>
                    <option value="active">Active</option>
                    <option value="inactive">Inactive</option>
                  </select>
                </div>
                <div className="input-group">
                  <label className="input-label">Role</label>
                  <select
                    className="input-control select-control"
                    value={editRole}
                    onChange={(e) => setEditRole(e.target.value as 'Student' | 'Teacher')}
                  >
                    <option value="Student">Student</option>
                    <option value="Teacher">Teacher</option>
                  </select>
                </div>
              </div>

              {/* Save Profile */}
              <button
                className="primary-btn"
                type="button"
                style={{ width: '100%', marginBottom: '1.25rem' }}
                onClick={() => void onUpdateUser()}
                disabled={busy}
              >
                {busy ? 'Saving...' : 'Save Profile Changes'}
              </button>

              {/* Reset Password accordion */}
              <div style={{ borderTop: '1px solid rgba(255,255,255,0.08)', paddingTop: '1rem' }}>
                <button
                  type="button"
                  className="secondary-btn"
                  style={{ width: '100%', justifyContent: 'space-between', display: 'flex' }}
                  onClick={() => { setShowPasswordReset((v) => !v); setEditNewPassword('') }}
                >
                  <span style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <span className="material-symbols-outlined" style={{ fontSize: '1.1rem' }}>lock_reset</span>
                    Reset Password
                  </span>
                  <span className="material-symbols-outlined">{showPasswordReset ? 'expand_less' : 'expand_more'}</span>
                </button>

                {showPasswordReset && (
                  <div style={{ marginTop: '1rem', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                    <p style={{ fontSize: '0.8rem', opacity: 0.7, margin: 0 }}>
                      Setting a new password will immediately revoke all active sessions for this user.
                    </p>
                    <div className="input-group">
                      <label className="input-label">New Password</label>
                      <div style={{ position: 'relative' }}>
                        <input
                          className="input-control"
                          type={editPasswordVisible ? 'text' : 'password'}
                          value={editNewPassword}
                          onChange={(e) => setEditNewPassword(e.target.value)}
                          placeholder="Min 8 characters"
                          style={{ paddingRight: '2.5rem' }}
                        />
                        <button
                          type="button"
                          onClick={() => setEditPasswordVisible((v) => !v)}
                          style={{
                            position: 'absolute', right: '0.75rem', top: '50%', transform: 'translateY(-50%)',
                            background: 'none', border: 'none', cursor: 'pointer', padding: 0,
                          }}
                        >
                          <span className="material-symbols-outlined" style={{ fontSize: '1.1rem', opacity: 0.6 }}>
                            {editPasswordVisible ? 'visibility_off' : 'visibility'}
                          </span>
                        </button>
                      </div>
                    </div>
                    <button
                      className="secondary-btn"
                      type="button"
                      onClick={() => void onResetPassword()}
                      disabled={busy || editNewPassword.length < 8}
                      style={{ color: 'var(--color-warning, #f59e0b)' }}
                    >
                      <span className="material-symbols-outlined">lock</span>
                      {busy ? 'Resetting...' : 'Confirm Password Reset'}
                    </button>
                  </div>
                )}
              </div>

              {/* Dismiss */}
              <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '1.25rem' }}>
                <button className="secondary-btn" type="button" onClick={() => { setEditingUser(null); setError(null) }}>Close</button>
              </div>
            </div>
          </div>
        )}
      </div>
      <Toaster toasts={toasts} onDismiss={dismiss} />
    </AdminLayout>
  )
}
