import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { destinationForRole, normalizeRole } from '../roleUtils'
import { useAuth } from '../store/AuthContext'
import type { UserSummary } from '../types'

type Mode = 'setup' | 'login'
type FieldErrors = Record<string, string>

function destinationForUser(user: UserSummary): string {
  if (normalizeRole(user.role) === 'Student') return '/login'
  return destinationForRole(user.role)
}

function validateEmail(email: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)
}

export function LandingPage() {
  const navigate = useNavigate()
  const { setup, isAuthenticated, login, user } = useAuth()

  const [mode, setMode] = useState<Mode>('login')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [showSetupPassword, setShowSetupPassword] = useState(false)
  const [showLoginPassword, setShowLoginPassword] = useState(false)

  // Setup Form
  const [institutionName, setInstitutionName] = useState('')
  const [setupUsername, setSetupUsername] = useState('')
  const [setupEmail, setSetupEmail] = useState('')
  const [setupPassword, setSetupPassword] = useState('')

  // Login Form
  const [usernameOrEmail, setUsernameOrEmail] = useState('')
  const [password, setPassword] = useState('')

  useEffect(() => {
    if (isAuthenticated && user) {
      navigate(destinationForUser(user), { replace: true })
    }
  }, [isAuthenticated, navigate, user])

  function switchMode(next: Mode) {
    setMode(next)
    setError(null)
    setFieldErrors({})
  }

  function validateSetup(): FieldErrors {
    const errors: FieldErrors = {}
    if (institutionName.trim().length < 2) errors.institutionName = 'Minimum 2 characters required'
    if (setupUsername.trim().length < 3) errors.username = 'Minimum 3 characters required'
    if (!validateEmail(setupEmail)) errors.email = 'Enter a valid email address'
    if (setupPassword.length < 8) errors.password = 'Minimum 8 characters required'
    return errors
  }

  function validateLogin(): FieldErrors {
    const errors: FieldErrors = {}
    if (!usernameOrEmail.trim()) errors.usernameOrEmail = 'Email or username is required'
    if (password.length < 8) errors.password = 'Minimum 8 characters required'
    return errors
  }

  async function onSetupSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const errors = validateSetup()
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors)
      return
    }
    setFieldErrors({})
    setBusy(true)
    setError(null)
    try {
      const nextUser = await setup({
        institutionName,
        username: setupUsername,
        email: setupEmail,
        password: setupPassword,
      })
      navigate(destinationForUser(nextUser), { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Setup failed')
    } finally {
      setBusy(false)
    }
  }

  async function onLoginSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const errors = validateLogin()
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors)
      return
    }
    setFieldErrors({})
    setBusy(true)
    setError(null)
    try {
      const nextUser = await login({ usernameOrEmail, password })
      if (normalizeRole(nextUser.role) === 'Student') {
        setError('Student accounts must use the Windows desktop exam app.')
        return
      }
      navigate(destinationForUser(nextUser), { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed')
    } finally {
      setBusy(false)
    }
  }

  const fieldError = (key: string) =>
    fieldErrors[key] ? (
      <p style={{ color: '#dc3545', fontSize: '0.75rem', marginTop: '4px', marginBottom: 0 }}>
        {fieldErrors[key]}
      </p>
    ) : null

  const errorBorder = (key: string): React.CSSProperties =>
    fieldErrors[key] ? { borderColor: '#dc3545' } : {}

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
            <p className="auth-subtitle" style={{ marginTop: '8px' }}>
              Sign in to manage an existing organization, or register a new one when you are setting up SmartExam for the first time.
            </p>
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
            <form onSubmit={onLoginSubmit} noValidate>
              <div className="input-group">
                <label className="input-label">Institution Access</label>
                <select className="input-control select-control" defaultValue="">
                  <option value="" disabled>Select Campus / Site</option>
                  <option value="main">Main Campus - Lab Alpha</option>
                </select>
                <p style={{ color: 'var(--on-surface-variant)', fontSize: '0.75rem', marginTop: '4px', marginBottom: 0 }}>Select your campus or site location</p>
              </div>

              <div className="input-group">
                <label className="input-label">Email or Username</label>
                <input
                  className="input-control"
                  placeholder="admin@smartexam.edu"
                  value={usernameOrEmail}
                  onChange={(e) => setUsernameOrEmail(e.target.value)}
                  style={errorBorder('usernameOrEmail')}
                />
                {fieldError('usernameOrEmail')}
              </div>

              <div className="input-group">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                  <label className="input-label" style={{ margin: 0 }}>Password</label>
                  <a href="#" style={{ fontSize: '0.75rem', color: 'var(--primary)', textDecoration: 'none' }}>Forgot?</a>
                </div>
                <div style={{ position: 'relative' }}>
                  <input
                    type={showLoginPassword ? 'text' : 'password'}
                    className="input-control"
                    placeholder="••••••••"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    style={errorBorder('password')}
                  />
                  <button
                    type="button"
                    onClick={() => setShowLoginPassword(!showLoginPassword)}
                    style={{
                      position: 'absolute',
                      right: '12px',
                      top: '50%',
                      transform: 'translateY(-50%)',
                      background: 'none',
                      border: 'none',
                      cursor: 'pointer',
                      color: 'var(--on-surface-variant)',
                      display: 'flex',
                      alignItems: 'center',
                      padding: 0
                    }}
                  >
                    <span className="material-symbols-outlined" style={{ fontSize: '20px' }}>
                      {showLoginPassword ? 'visibility' : 'visibility_off'}
                    </span>
                  </button>
                </div>
                {fieldError('password')}
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
                <p style={{ fontSize: '0.85rem', color: 'var(--on-surface-variant)', marginBottom: '8px' }}>
                  Don't have an organization yet?
                </p>
                <button
                  type="button"
                  onClick={() => switchMode('setup')}
                  style={{ border: 'none', background: 'none', color: 'var(--primary)', fontWeight: 600, cursor: 'pointer', padding: 0, fontSize: 'inherit' }}
                >
                  Create a new organization
                </button>
              </div>
            </form>
          ) : (
            <form onSubmit={onSetupSubmit} noValidate>
              <div className="input-group">
                <label className="input-label">Organization Name</label>
                <input
                  className="input-control"
                  placeholder="e.g. University of Technology"
                  value={institutionName}
                  onChange={(e) => setInstitutionName(e.target.value)}
                  style={errorBorder('institutionName')}
                />
                {fieldError('institutionName')}
              </div>

              <div className="security-banner">
                <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>info</span>
                <span>This name will appear on all dashboards and reports.</span>
              </div>

              <div className="input-group">
                <label className="input-label">Admin Username</label>
                <input
                  className="input-control"
                  placeholder="j.smith"
                  value={setupUsername}
                  onChange={(e) => setSetupUsername(e.target.value)}
                  style={errorBorder('username')}
                />
                {fieldError('username')}
              </div>

              <div className="input-group">
                <label className="input-label">Admin Email</label>
                <input
                  type="email"
                  className="input-control"
                  placeholder="admin@institution.edu"
                  value={setupEmail}
                  onChange={(e) => setSetupEmail(e.target.value)}
                  style={errorBorder('email')}
                />
                {fieldError('email')}
              </div>

              <div className="input-group">
                <label className="input-label">Master Password</label>
                <div style={{ position: 'relative' }}>
                  <input
                    type={showSetupPassword ? 'text' : 'password'}
                    className="input-control"
                    placeholder="Min. 8 characters"
                    value={setupPassword}
                    onChange={(e) => setSetupPassword(e.target.value)}
                    style={errorBorder('password')}
                  />
                  <button
                    type="button"
                    onClick={() => setShowSetupPassword(!showSetupPassword)}
                    style={{
                      position: 'absolute',
                      right: '12px',
                      top: '50%',
                      transform: 'translateY(-50%)',
                      background: 'none',
                      border: 'none',
                      cursor: 'pointer',
                      color: 'var(--on-surface-variant)',
                      display: 'flex',
                      alignItems: 'center',
                      padding: 0
                    }}
                  >
                    <span className="material-symbols-outlined" style={{ fontSize: '20px' }}>
                      {showSetupPassword ? 'visibility' : 'visibility_off'}
                    </span>
                  </button>
                </div>
                {fieldErrors.password
                  ? fieldError('password')
                  : <p style={{ color: 'var(--on-surface-variant)', fontSize: '0.75rem', marginTop: '4px', marginBottom: 0 }}>Minimum 8 characters</p>
                }
              </div>

              <button className="btn btn-primary" type="submit" disabled={busy}>
                {busy ? 'Creating organization...' : 'Create Organization & Admin'}
                {!busy && <span className="material-symbols-outlined">add_business</span>}
              </button>

              <div style={{ marginTop: '20px' }}>
                <button
                  type="button"
                  onClick={() => switchMode('login')}
                  style={{ border: 'none', background: 'none', color: 'var(--primary)', fontSize: '0.85rem', cursor: 'pointer', fontWeight: 500, padding: 0 }}
                >
                  ← Back to Sign In
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
