import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { destinationForRole } from '../roleUtils'
import { useAuth } from '../store/AuthContext'
import type { UserSummary } from '../types'

type FieldErrors = Record<string, string>

function destinationForUser(user: UserSummary): string {
  return destinationForRole(user.role)
}

function validateEmail(email: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)
}

export default function SetupPage() {
  const navigate = useNavigate()
  const { setup, isPlatformBootstrapped } = useAuth()
  const [step, setStep] = useState(1)
  const [loading, setLoading] = useState(false)
  const [formData, setFormData] = useState({
    institutionName: '',
    username: '',
    email: '',
    password: '',
    confirmPassword: ''
  })
  const [error, setError] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})

  useEffect(() => {
    if (isPlatformBootstrapped === true) {
      navigate('/login')
    }
  }, [isPlatformBootstrapped, navigate])

  function validateStep1(): FieldErrors {
    const errors: FieldErrors = {}
    if (formData.institutionName.trim().length < 2) errors.institutionName = 'Minimum 2 characters required'
    return errors
  }

  function validateStep2(): FieldErrors {
    const errors: FieldErrors = {}
    if (formData.username.trim().length < 3) errors.username = 'Minimum 3 characters required'
    if (!validateEmail(formData.email)) errors.email = 'Enter a valid email address'
    if (formData.password.length < 8) errors.password = 'Minimum 8 characters required'
    if (formData.confirmPassword !== formData.password) errors.confirmPassword = 'Passwords do not match'
    return errors
  }

  function goToStep2() {
    const errors = validateStep1()
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors)
      return
    }
    setFieldErrors({})
    setStep(2)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (step === 1) {
      goToStep2()
      return
    }

    const errors = validateStep2()
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors)
      return
    }
    setFieldErrors({})
    setLoading(true)
    setError('')

    try {
      const nextUser = await setup({
        institutionName: formData.institutionName,
        username: formData.username,
        email: formData.email,
        password: formData.password
      })
      navigate(destinationForUser(nextUser))
    } catch (err: any) {
      setError(err.message || 'Setup failed')
    } finally {
      setLoading(false)
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

  if (isPlatformBootstrapped === null) {
    return (
      <div className="auth-page" style={{ justifyContent: 'center', alignItems: 'center' }}>
        <p style={{ color: 'var(--primary)', fontWeight: 500 }}>Initializing SmartExam Systems...</p>
      </div>
    )
  }

  return (
    <div className="auth-page">
      <div className="auth-card">
        <header className="auth-header">
          <div className="auth-logo-wrap">
            <span className="material-symbols-outlined icon-fill">shield_person</span>
          </div>
          <h1 className="auth-title">Platform Initialization</h1>
          <p className="auth-subtitle">Step {step} of 2: {step === 1 ? 'Institution Details' : 'Organization Admin Account'}</p>

          <div style={{ display: 'flex', justifyContent: 'center', gap: '8px', marginTop: '16px' }}>
            <div style={{ width: '32px', height: '4px', borderRadius: '2px', background: step >= 1 ? 'var(--primary)' : 'var(--surface-alt)' }}></div>
            <div style={{ width: '32px', height: '4px', borderRadius: '2px', background: step >= 2 ? 'var(--primary)' : 'var(--surface-alt)' }}></div>
          </div>
        </header>

        {error && (
          <div style={{ background: '#f8d7da', color: '#721c24', padding: '12px', borderRadius: '8px', marginBottom: '20px', fontSize: '0.85rem' }}>
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate>
          {step === 1 ? (
            <div className="animate-in">
              <div className="input-group">
                <label className="input-label">Institution Name</label>
                <input
                  className="input-control"
                  placeholder="e.g. Global Tech University"
                  value={formData.institutionName}
                  onChange={e => setFormData({ ...formData, institutionName: e.target.value })}
                  style={errorBorder('institutionName')}
                />
                {fieldError('institutionName')}
              </div>

              <div className="security-banner">
                <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>info</span>
                <span>This name will be displayed on all exam headers.</span>
              </div>

              <button type="button" className="btn btn-primary" onClick={goToStep2}>
                Next Step
                <span className="material-symbols-outlined">arrow_forward</span>
              </button>
            </div>
          ) : (
            <div className="animate-in">
              <div className="input-group">
                <label className="input-label">Username</label>
                <input
                  className="input-control"
                  placeholder="admin"
                  value={formData.username}
                  onChange={e => setFormData({ ...formData, username: e.target.value })}
                  style={errorBorder('username')}
                />
                {fieldError('username')}
              </div>

              <div className="input-group">
                <label className="input-label">Email Address</label>
                <input
                  className="input-control"
                  type="email"
                  placeholder="admin@institution.edu"
                  value={formData.email}
                  onChange={e => setFormData({ ...formData, email: e.target.value })}
                  style={errorBorder('email')}
                />
                {fieldError('email')}
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                <div className="input-group">
                  <label className="input-label">Password</label>
                  <input
                    className="input-control"
                    type="password"
                    placeholder="Min. 8 characters"
                    value={formData.password}
                    onChange={e => setFormData({ ...formData, password: e.target.value })}
                    style={errorBorder('password')}
                  />
                  {fieldErrors.password
                    ? fieldError('password')
                    : <p style={{ color: 'var(--on-surface-variant)', fontSize: '0.75rem', marginTop: '4px', marginBottom: 0 }}>Min. 8 characters</p>
                  }
                </div>
                <div className="input-group">
                  <label className="input-label">Confirm</label>
                  <input
                    className="input-control"
                    type="password"
                    placeholder="Repeat password"
                    value={formData.confirmPassword}
                    onChange={e => setFormData({ ...formData, confirmPassword: e.target.value })}
                    style={errorBorder('confirmPassword')}
                  />
                  {fieldError('confirmPassword')}
                </div>
              </div>

              <div style={{ display: 'flex', gap: '12px', marginTop: '12px' }}>
                <button type="button" className="btn btn-secondary" style={{ flex: '0 0 80px' }} onClick={() => { setStep(1); setFieldErrors({}) }}>
                  Back
                </button>
                <button type="submit" className="btn btn-primary" disabled={loading}>
                  {loading ? 'Finalizing...' : 'Complete Setup'}
                </button>
              </div>
            </div>
          )}
        </form>
      </div>

      <footer className="auth-footer">
        <p>© 2026 SMARTEXAM GLOBAL SYSTEMS</p>
      </footer>
    </div>
  )
}
