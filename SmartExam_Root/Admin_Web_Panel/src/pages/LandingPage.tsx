import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../store/AuthContext'

type Mode = 'bootstrap' | 'login'

export function LandingPage() {
  const navigate = useNavigate()
  const { bootstrap, isAuthenticated, login } = useAuth()

  const [mode, setMode] = useState<Mode>('bootstrap')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('Secure Session Enabled (AES-256). All access is monitored.')

  const [institutionName, setInstitutionName] = useState('')
  const [setupUsername, setSetupUsername] = useState('')
  const [setupEmail, setSetupEmail] = useState('')
  const [setupPassword, setSetupPassword] = useState('')

  const [usernameOrEmail, setUsernameOrEmail] = useState('')
  const [password, setPassword] = useState('')

  useEffect(() => {
    if (isAuthenticated) {
      navigate('/admin', { replace: true })
    }
  }, [isAuthenticated, navigate])

  async function onBootstrapSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      await bootstrap({
        institutionName,
        username: setupUsername,
        email: setupEmail,
        password: setupPassword,
      })
      navigate('/admin', { replace: true })
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Bootstrap failed')
    } finally {
      setBusy(false)
    }
  }

  async function onLoginSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      await login({ usernameOrEmail, password })
      navigate('/admin', { replace: true })
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Login failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="auth-page">
      <nav className="auth-nav">
        <div className="nav-left">
          <span className="material-symbols-outlined icon-fill">security</span>
          <span className="nav-title">SmartExam Control Center</span>
        </div>
        <div className="nav-right">
          <button className="nav-btn">
            <span className="material-symbols-outlined">language</span>
            English
          </button>
          <button className="nav-btn">
            <span className="material-symbols-outlined">help_outline</span>
            Support
          </button>
        </div>
      </nav>

      <main className="auth-main">
        <div className="glass-card auth-card">
          <header className="auth-header">
            <div className="auth-icon-wrap">
              <span className="material-symbols-outlined icon-large icon-fill">
                {mode === 'bootstrap' ? 'domain_add' : 'shield_person'}
              </span>
            </div>
            <h1>SmartExam Control Center</h1>
            <div className="mode-pill">
              <span className="dot"></span>
              {mode === 'bootstrap' ? 'INSTITUTION SETUP' : 'TEACHER LOGIN'}
            </div>
          </header>

          <div className="auth-mode-toggle">
            <button
              className={mode === 'bootstrap' ? 'active' : ''}
              onClick={() => {
                setMode('bootstrap')
                setMessage('Set up your institution to get started.')
              }}
            >
              Register
            </button>
            <button
              className={mode === 'login' ? 'active' : ''}
              onClick={() => {
                setMode('login')
                setMessage('Sign in to access your dashboard.')
              }}
            >
              Login
            </button>
          </div>

          {mode === 'bootstrap' ? (
            <form className="auth-form-v2" onSubmit={onBootstrapSubmit}>
              <div className="input-group">
                <label>Institution Name</label>
                <input
                  placeholder="e.g. Global University"
                  value={institutionName}
                  onChange={(e) => setInstitutionName(e.target.value)}
                  required
                />
              </div>
              <div className="input-grid">
                <div className="input-group">
                  <label>Admin Username</label>
                  <input
                    placeholder="e.g. admin_pro"
                    value={setupUsername}
                    onChange={(e) => setSetupUsername(e.target.value)}
                    required
                  />
                </div>
                <div className="input-group">
                  <label>Admin Email</label>
                  <input
                    type="email"
                    placeholder="admin@institution.edu"
                    value={setupEmail}
                    onChange={(e) => setSetupEmail(e.target.value)}
                    required
                  />
                </div>
              </div>
              <div className="input-group">
                <label>Password</label>
                <input
                  type="password"
                  placeholder="••••••••"
                  value={setupPassword}
                  onChange={(e) => setSetupPassword(e.target.value)}
                  minLength={8}
                  required
                />
              </div>

              <button className="auth-submit" type="submit" disabled={busy}>
                {busy ? 'Creating...' : 'Initialize Control Center'}
                <span className="material-symbols-outlined">rocket_launch</span>
              </button>
            </form>
          ) : (
            <form className="auth-form-v2" onSubmit={onLoginSubmit}>
              <div className="input-group">
                <label>Email or Username</label>
                <input
                  placeholder="e.g. j.smith@university.edu"
                  value={usernameOrEmail}
                  onChange={(e) => setUsernameOrEmail(e.target.value)}
                  required
                />
              </div>
              <div className="input-group">
                <div className="label-row">
                  <label>Password</label>
                  <a href="#" className="forgot-link">
                    Forgot?
                  </a>
                </div>
                <input
                  type="password"
                  placeholder="••••••••"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  minLength={8}
                  required
                />
              </div>

              <div className="security-status">
                <div className="status-indicator">
                  <span className="pulse"></span>
                  <span className="dot-static"></span>
                </div>
                <span className="status-text">LAN SECURE</span>
              </div>

              <button className="auth-submit" type="submit" disabled={busy}>
                {busy ? 'Authenticating...' : 'Authenticate Access'}
                <span className="material-symbols-outlined">arrow_forward</span>
              </button>
            </form>
          )}

          <footer className="auth-card-footer">
            <p>{message}</p>
          </footer>
        </div>
      </main>

      <footer className="page-footer">
        <div className="footer-copyright">© 2026 SmartExam Systems. Secured by Deep Azure Encryption.</div>
        <div className="footer-links">
          <a href="#">Privacy Policy</a>
          <a href="#">Terms of Service</a>
          <a href="#">Security Whitepaper</a>
        </div>
      </footer>

      <div className="blob blob-1"></div>
      <div className="blob blob-2"></div>
    </div>
  )
}