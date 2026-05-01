import { useState, useEffect } from 'react'
import { AdminLayout } from '../components/AdminLayout'
import { useAuth } from '../store/AuthContext'

export default function SuperAdminDashboard() {
  const [institutions, setInstitutions] = useState([])
  const [loading, setLoading] = useState(true)
  const { accessToken } = useAuth()

  useEffect(() => {
    fetchInstitutions()
  }, [])

  const fetchInstitutions = async () => {
    try {
      const res = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/super-admin/institutions`, {
        headers: { 'Authorization': `Bearer ${accessToken}` }
      })
      const data = await res.json()
      if (data.success) {
        setInstitutions(data.data)
      }
    } catch (err) {
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Platform Control Plane</p>
            <h1>Institutional Overview</h1>
            <p className="subtext">Manage all registered organizations and their administrative reach.</p>
          </div>
          <div className="header-actions">
            <button className="primary-btn">
              <span className="material-symbols-outlined">add_business</span>
              Register New Institution
            </button>
          </div>
        </header>

        <div className="stats-row" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '1.5rem', marginBottom: '2rem' }}>
          <div className="glass-card stat-card">
            <div className="stat-label">Total Organizations</div>
            <div className="stat-value">{institutions.length}</div>
          </div>
          <div className="glass-card stat-card">
            <div className="stat-label">Active Subscriptions</div>
            <div className="stat-value">{institutions.length}</div>
          </div>
          <div className="glass-card stat-card">
            <div className="stat-label">System Health</div>
            <div className="stat-value" style={{ color: '#2ecc71' }}>Optimal</div>
          </div>
        </div>

        <div className="glass-card" style={{ padding: 0, overflow: 'hidden' }}>
          <table className="data-table">
            <thead>
              <tr>
                <th>Institution Name</th>
                <th>Contact Email</th>
                <th>Status</th>
                <th>Created At</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', padding: '2rem' }}>Loading institutions...</td></tr>
              ) : institutions.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', padding: '2rem' }}>No institutions registered yet.</td></tr>
              ) : institutions.map((inst: any) => (
                <tr key={inst.id}>
                  <td>
                    <div style={{ fontWeight: 600 }}>{inst.name}</div>
                    <div style={{ fontSize: '0.75rem', opacity: 0.6 }}>{inst.id}</div>
                  </td>
                  <td>{inst.contactEmail}</td>
                  <td><span className="status-pill active">Active</span></td>
                  <td>{new Date(inst.createdAtUtc).toLocaleDateString()}</td>
                  <td>
                    <div style={{ display: 'flex', gap: '0.5rem' }}>
                      <button className="text-btn primary">Manage</button>
                      <button className="text-btn secondary">Audit</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </AdminLayout>
  )
}
