import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../store/AuthContext'

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

  useEffect(() => {
    if (isPlatformBootstrapped === true) {
      navigate('/login')
    }
  }, [isPlatformBootstrapped, navigate])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    
    // If user presses Enter on Step 1, just move to Step 2
    if (step === 1) {
      if (formData.institutionName.trim()) {
        setStep(2);
      }
      return;
    }

    if (formData.password !== formData.confirmPassword) {
      setError('Passwords do not match')
      return
    }

    setLoading(true)
    setError('')

    try {
      await setup({
        institutionName: formData.institutionName,
        username: formData.username,
        email: formData.email,
        password: formData.password
      })
      navigate('/admin')
    } catch (err: any) {
      setError(err.message || 'Setup failed')
    } finally {
      setLoading(false)
    }
  }

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
          <p className="auth-subtitle">Step {step} of 2: {step === 1 ? 'Institution Details' : 'Super Admin Account'}</p>
          
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

        <form onSubmit={handleSubmit}>
          {step === 1 ? (
            <div className="animate-in">
              <div className="input-group">
                <label className="input-label">Institution Name</label>
                <input 
                  className="input-control"
                  required
                  placeholder="e.g. Global Tech University"
                  value={formData.institutionName}
                  onChange={e => setFormData({...formData, institutionName: e.target.value})}
                />
              </div>
              
              <div className="security-banner">
                <span className="material-symbols-outlined" style={{ fontSize: '18px' }}>info</span>
                <span>This name will be displayed on all exam headers.</span>
              </div>

              <button type="button" className="btn btn-primary" onClick={() => setStep(2)}>
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
                  required
                  placeholder="admin"
                  value={formData.username}
                  onChange={e => setFormData({...formData, username: e.target.value})}
                />
              </div>

              <div className="input-group">
                <label className="input-label">Email Address</label>
                <input 
                  className="input-control"
                  required
                  type="email"
                  placeholder="admin@institution.edu"
                  value={formData.email}
                  onChange={e => setFormData({...formData, email: e.target.value})}
                />
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                <div className="input-group">
                  <label className="input-label">Password</label>
                  <input 
                    className="input-control"
                    required
                    type="password"
                    value={formData.password}
                    onChange={e => setFormData({...formData, password: e.target.value})}
                  />
                </div>
                <div className="input-group">
                  <label className="input-label">Confirm</label>
                  <input 
                    className="input-control"
                    required
                    type="password"
                    value={formData.confirmPassword}
                    onChange={e => setFormData({...formData, confirmPassword: e.target.value})}
                  />
                </div>
              </div>

              <div style={{ display: 'flex', gap: '12px', marginTop: '12px' }}>
                <button type="button" className="btn btn-secondary" style={{ flex: '0 0 80px' }} onClick={() => setStep(1)}>
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
