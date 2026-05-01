import { useEffect, useState } from 'react'
import { createInstitution, createInstitutionAdmin, getInstitutions } from '../api/superAdmin'
import { AdminLayout } from '../components/AdminLayout'
import type { InstitutionListItem } from '../types'

function validEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

export default function SuperAdminDashboard() {
  const [institutions, setInstitutions] = useState<InstitutionListItem[]>([])
  const [selectedInstitutionId, setSelectedInstitutionId] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [institutionName, setInstitutionName] = useState('')
  const [contactEmail, setContactEmail] = useState('')
  const [adminUsername, setAdminUsername] = useState('')
  const [adminEmail, setAdminEmail] = useState('')
  const [adminPassword, setAdminPassword] = useState('')

  async function loadInstitutions() {
    const data = await getInstitutions()
    setInstitutions(data)
    if (!selectedInstitutionId && data.length > 0) {
      setSelectedInstitutionId(data[0].id)
    }
  }

  useEffect(() => {
    void loadInstitutions()
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load institutions.'))
      .finally(() => setLoading(false))
  }, [])

  async function onCreateInstitution(event: React.FormEvent) {
    event.preventDefault()
    if (institutionName.trim().length < 2) {
      setError('Institution name must be at least 2 characters.')
      return
    }
    if (!validEmail(contactEmail)) {
      setError('Enter a valid institution contact email.')
      return
    }

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const institution = await createInstitution({
        name: institutionName.trim(),
        contactEmail: contactEmail.trim().toLowerCase(),
        logoUrl: null,
        allowedIpRanges: null,
        enforceSingleDeviceBinding: true,
        allowTeacherResetBinding: true,
        sessionTimeoutMinutes: 30,
      })
      setInstitutionName('')
      setContactEmail('')
      await loadInstitutions()
      setSelectedInstitutionId(institution.id)
      setNotice('Institution created. Now create its organization admin.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create institution.')
    } finally {
      setBusy(false)
    }
  }

  async function onCreateAdmin(event: React.FormEvent) {
    event.preventDefault()
    if (!selectedInstitutionId) {
      setError('Select an institution first.')
      return
    }
    if (adminUsername.trim().length < 3) {
      setError('Admin username must be at least 3 characters.')
      return
    }
    if (!validEmail(adminEmail)) {
      setError('Enter a valid admin email.')
      return
    }
    if (adminPassword.length < 8) {
      setError('Admin password must be at least 8 characters.')
      return
    }

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await createInstitutionAdmin(selectedInstitutionId, {
        username: adminUsername.trim(),
        email: adminEmail.trim().toLowerCase(),
        password: adminPassword,
      })
      setAdminUsername('')
      setAdminEmail('')
      setAdminPassword('')
      setNotice('Organization admin created. That admin can log in and create teachers/students.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create organization admin.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Platform Control Plane</p>
            <h1>Institutions & Admins</h1>
            <p className="subtext">Create institutions and assign organization admins who manage teachers and students.</p>
          </div>
          <span className="status-badge secure">{institutions.length} institutions</span>
        </header>

        {notice && <div className="inline-alert">{notice}</div>}
        {error && <div className="inline-alert error">{error}</div>}

        <div className="dashboard-main-grid">
          <section className="glass-card form-section">
            <div className="section-title-row">
              <span className="material-symbols-outlined">add_business</span>
              <h3>Register Institution</h3>
            </div>
            <form onSubmit={onCreateInstitution}>
              <div className="input-group">
                <label className="input-label">Institution Name</label>
                <input className="input-control" value={institutionName} onChange={(e) => setInstitutionName(e.target.value)} placeholder="Global Tech University" />
              </div>
              <div className="input-group">
                <label className="input-label">Contact Email</label>
                <input className="input-control" value={contactEmail} onChange={(e) => setContactEmail(e.target.value)} placeholder="admin@institution.edu" />
              </div>
              <button className="primary-btn" type="submit" disabled={busy}>
                <span className="material-symbols-outlined">add</span>
                Create Institution
              </button>
            </form>
          </section>

          <section className="glass-card form-section">
            <div className="section-title-row">
              <span className="material-symbols-outlined">admin_panel_settings</span>
              <h3>Create Organization Admin</h3>
            </div>
            <form onSubmit={onCreateAdmin}>
              <div className="input-group">
                <label className="input-label">Institution</label>
                <select className="input-control select-control" value={selectedInstitutionId} onChange={(e) => setSelectedInstitutionId(e.target.value)}>
                  <option value="">Select institution</option>
                  {institutions.map((institution) => (
                    <option key={institution.id} value={institution.id}>{institution.name}</option>
                  ))}
                </select>
              </div>
              <div className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Username</label>
                  <input className="input-control" value={adminUsername} onChange={(e) => setAdminUsername(e.target.value)} placeholder="orgadmin" />
                </div>
                <div className="input-group">
                  <label className="input-label">Email</label>
                  <input className="input-control" value={adminEmail} onChange={(e) => setAdminEmail(e.target.value)} placeholder="orgadmin@institution.edu" />
                </div>
              </div>
              <div className="input-group">
                <label className="input-label">Password</label>
                <input className="input-control" type="password" value={adminPassword} onChange={(e) => setAdminPassword(e.target.value)} placeholder="Minimum 8 characters" />
              </div>
              <button className="primary-btn" type="submit" disabled={busy}>
                <span className="material-symbols-outlined">person_add</span>
                Create Admin
              </button>
            </form>
          </section>
        </div>

        <section className="glass-card table-container">
          <div className="table-header-row">
            <h3>Registered Institutions</h3>
            <button className="secondary-btn" type="button" onClick={() => void loadInstitutions()}>
              <span className="material-symbols-outlined">refresh</span>
              Refresh
            </button>
          </div>
          <div className="table-wrapper">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Institution Name</th>
                  <th>Contact Email</th>
                  <th>Session Timeout</th>
                  <th>Created At</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <tr><td colSpan={5} className="empty-state">Loading institutions...</td></tr>
                ) : institutions.length === 0 ? (
                  <tr><td colSpan={5} className="empty-state">No institutions registered yet.</td></tr>
                ) : institutions.map((institution) => (
                  <tr key={institution.id}>
                    <td>
                      <div className="student-details">
                        <span className="student-name">{institution.name}</span>
                        <span className="student-email">{institution.id}</span>
                      </div>
                    </td>
                    <td>{institution.contactEmail}</td>
                    <td>{institution.sessionTimeoutMinutes} minutes</td>
                    <td>{new Date(institution.createdAtUtc).toLocaleDateString()}</td>
                    <td><span className="status-pill active">Active</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </AdminLayout>
  )
}
