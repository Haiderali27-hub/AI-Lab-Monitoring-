import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { destinationForRole, normalizeRole } from '../roleUtils'
import { useAuth } from '../store/AuthContext'
import type { SystemRole } from '../types'

type ProtectedRouteProps = {
  children: React.ReactNode
  roles?: SystemRole[]
}

export function ProtectedRoute({ children, roles }: ProtectedRouteProps) {
  const { isAuthenticated, user, logout } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const userRole = normalizeRole(user?.role)

  async function backToLogin() {
    await logout()
    navigate('/login', { replace: true })
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  if (roles && (!userRole || !roles.includes(userRole))) {
    const target = destinationForRole(userRole)
    if (location.pathname !== target) {
      return <Navigate to={target} replace />
    }

    return (
      <div className="auth-page" style={{ justifyContent: 'center', alignItems: 'center', padding: '2rem' }}>
        <div className="auth-card">
          <div className="auth-logo-wrap">
            <span className="material-symbols-outlined">lock</span>
          </div>
          <h1 className="auth-title">Access Restricted</h1>
          <p className="auth-subtitle">
            Your current role cannot open this page.
            <br />
            Signed in as: {userRole ?? String(user?.role ?? 'unknown')}
          </p>
          <button className="btn btn-primary" type="button" onClick={() => void backToLogin()} style={{ marginTop: '1.5rem' }}>
            Back to Login
          </button>
        </div>
      </div>
    )
  }

  return <>{children}</>
}
