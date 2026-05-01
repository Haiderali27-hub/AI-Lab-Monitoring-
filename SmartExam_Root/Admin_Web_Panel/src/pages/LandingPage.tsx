import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../store/AuthContext'

type Mode = 'setup' | 'login'

export function LandingPage() {
  const navigate = useNavigate()
  const { setup, isAuthenticated, login } = useAuth()

  const [mode, setMode] = useState<Mode>('login')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Setup Form
  const [institutionName, setInstitutionName] = useState('')
  const [setupUsername, setSetupUsername] = useState('')
  const [setupEmail, setSetupEmail] = useState('')
  const [setupPassword, setSetupPassword] = useState('')

  // Login Form
  const [usernameOrEmail, setUsernameOrEmail] = useState('')
  const [password, setPassword] = useState('')

  useEffect(() => {
    if (isAuthenticated) {
      navigate('/admin', { replace: true })
    }
  }, [isAuthenticated, navigate])

  async function onSetupSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await setup({
        institutionName,
        username: setupUsername,
        email: setupEmail,
        password: setupPassword,
      })
      navigate('/admin', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Setup failed')
    } finally {
      setBusy(false)
    }
  }

  async function onLoginSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login({ usernameOrEmail, password })
      navigate('/admin', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="auth-page">
      <div className="auth-shell">
        <header className="auth-shell-header">
          <div className="auth-shell-title">
            <span className="material-symbols-outlined">admin_panel_settings</span>
            <span>Admin Login & Institution Access</span>
          </div>
          <div className="auth-shell-actions">
            <button type="button">English</button>
            <button type="button">Support</button>
            <button type="button">Connectivity</button>
          </div>
        </header>

        <div className="auth-card">
          <div className="auth-header">
            <div className="auth-logo-wrap">
              <span className="material-symbols-outlined icon-fill">shield_person</span>
            </div>
            <h1 className="auth-title">SmartExam Control Center</h1>
            <div style={{ display: 'flex', justifyContent: 'center', gap: '8px', marginTop: '8px' }}>
              <span className="status-badge secure">LAN SECURE</span>
              <span className="status-badge secure">SYSTEM ADMIN</span>
            </div>
          </div>

          {error && (
            <div style={{ background: '#f8d7da', color: '#721c24', padding: '12px', borderRadius: '8px', marginBottom: '20px', fontSize: '0.85rem' }}>
              {error}
            </div>
          )}

          {mode === 'login' ? (
            <form onSubmit={onLoginSubmit}>
              <div className="input-group">
                <label className="input-label">Institution Access</label>
                <select className="input-control select-control" defaultValue="">
                  <option value="" disabled>Select Campus / Site</option>
                  <option value="main">Main Campus - Lab Alpha</option>
                </select>
              </div>

              <div className="input-group">
                <label className="input-label">Email or Username</label>
                <input
                  className="input-control"
                  placeholder="admin@smartexam.edu"
                  value={usernameOrEmail}
                  onChange={(e) => setUsernameOrEmail(e.target.value)}
                  required
                />
              </div>

              <div className="input-group">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                  <label className="input-label" style={{ margin: 0 }}>Password</label>
                  <a href="#" style={{ fontSize: '0.75rem', color: 'var(--primary)', textDecoration: 'none' }}>Forgot?</a>
                </div>
                <input
                  type="password"
                  className="input-control"
                  placeholder="••••••••"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                />
              </div>

              <div className="security-banner">
                <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>lock</span>
                <span>Secure Session Enabled (AES-256)</span>
              </div>

              <button className="btn btn-primary" type="submit" disabled={busy}>
                {busy ? 'Authenticating...' : 'Authenticate Access'}
                {!busy && <span className="material-symbols-outlined">arrow_forward</span>}
              </button>

              <div style={{ marginTop: '20px' }}>
                <p style={{ fontSize: '0.85rem', color: 'var(--on-surface-variant)' }}>
                  New institution? <button type="button" onClick={() => setMode('setup')} style={{ border: 'none', background: 'none', color: 'var(--primary)', fontWeight: 600, cursor: 'pointer' }}>Register here</button>
                </p>
              </div>
            </form>
          ) : (
            <form onSubmit={onSetupSubmit}>
              <div className="input-group">
                <label className="input-label">Institution Name</label>
                <input
                  className="input-control"
                  placeholder="e.g. University of Technology"
                  value={institutionName}
                  onChange={(e) => setInstitutionName(e.target.value)}
                  required
                />
              </div>

              <div className="input-group">
                <label className="input-label">Admin Username</label>
                <input
                  className="input-control"
                  placeholder="j.smith"
                  value={setupUsername}
                  onChange={(e) => setSetupUsername(e.target.value)}
                  required
                />
              </div>

              <div className="input-group">
                <label className="input-label">Admin Email</label>
                <input
                  type="email"
                  className="input-control"
                  placeholder="admin@institution.edu"
                  value={setupEmail}
                  onChange={(e) => setSetupEmail(e.target.value)}
                  required
                />
              </div>

              <div className="input-group">
                <label className="input-label">Master Password</label>
                <input
                  type="password"
                  className="input-control"
                  placeholder="••••••••"
                  value={setupPassword}
                  onChange={(e) => setSetupPassword(e.target.value)}
                  required
                />
              </div>

              <button className="btn btn-primary" type="submit" disabled={busy}>
                {busy ? 'Setting up...' : 'Create Platform Admin'}
                {!busy && <span className="material-symbols-outlined">add_business</span>}
              </button>

              <div style={{ marginTop: '20px' }}>
                <button type="button" onClick={() => setMode('login')} style={{ border: 'none', background: 'none', color: 'var(--neutral)', fontSize: '0.85rem', cursor: 'pointer' }}>
                  Back to Login
                </button>
              </div>
            </form>
          )}
        </div>
      </div>

      <footer className="auth-footer">
        <p>© 2026 SMARTEXAM GLOBAL SYSTEMS</p>
        <div className="footer-links">
          <a href="#">Version 4.2.0-LTS</a>
          <a href="#">Help Center</a>
          <a href="#">Terms</a>
          <a href="#">Privacy</a>
        </div>
      </footer>
    </div>
  )
}