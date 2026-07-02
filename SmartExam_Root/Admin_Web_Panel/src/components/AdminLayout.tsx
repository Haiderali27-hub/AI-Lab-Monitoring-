import type { ReactNode } from 'react'
import { useAuth } from '../store/AuthContext'
import { useNavigate, useLocation, Link } from 'react-router-dom'
import { normalizeRole } from '../roleUtils'

type AdminLayoutProps = {
  children: ReactNode
}

export function AdminLayout({ children }: AdminLayoutProps) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  async function onLogout() {
    await logout()
    navigate('/login')
  }

  const role = normalizeRole(user?.role)
  const menuItems = role === 'SuperAdmin'
    ? [
        { label: 'Institutions', icon: 'business_center', path: '/super-admin' },
        { label: 'Institution Users', icon: 'group', path: '/admin/users' },
      ]
    : [
        { label: 'Dashboard', icon: 'dashboard', path: '/admin' },
        { label: 'User Management', icon: 'group', path: '/admin/users' },
        { label: 'Exam Management', icon: 'assignment', path: '/admin/exams' },
        { label: 'Labs & Machines', icon: 'desktop_windows', path: '/admin/labs' },
        { label: 'Security Logs', icon: 'history_edu', path: '/admin/logs' },
        { label: 'Reports', icon: 'assessment', path: '/admin/reports' },
      ]

  return (
    <div className="admin-page">
      <aside className="admin-sidebar">
        <div className="sidebar-header">
          <div className="brand-icon">
            <span className="material-symbols-outlined icon-fill">shield_person</span>
          </div>
          <div className="brand-text">
            <span className="brand-name">SmartExam</span>
            <span className="brand-tag">Admin Console</span>
          </div>
        </div>

        <nav className="sidebar-nav">
          {menuItems.map((item) => (
            <Link
              key={item.path}
              to={item.path}
              className={`nav-item ${location.pathname === item.path ? 'active' : ''}`}
            >
              <span className="material-symbols-outlined">{item.icon}</span>
              <span>{item.label}</span>
            </Link>
          ))}
          {user?.role !== 'SuperAdmin' && (
            <Link to="/admin/settings" className={`nav-item ${location.pathname === '/admin/settings' ? 'active' : ''}`}>
              <span className="material-symbols-outlined">settings</span>
              <span>Settings</span>
            </Link>
          )}
        </nav>

        <div className="sidebar-footer">
          <button className="nav-item logout-btn" onClick={onLogout}>
            <span className="material-symbols-outlined">logout</span>
            <span>Logout</span>
          </button>
        </div>
      </aside>

      <main className="admin-main">
        <header className="admin-top-nav">
          <div className="top-nav-left">
            <span className="status-badge secure">{user?.role ?? 'User'}</span>
          </div>

          <div className="top-nav-right">
            <div className="user-profile">
              <div>
                <div style={{ fontWeight: 700, color: 'var(--neutral)' }}>{user?.username}</div>
                <div style={{ fontSize: '0.75rem', color: 'var(--on-surface-variant)' }}>{user?.email}</div>
              </div>
              <div className="profile-img">
                {user?.username?.charAt(0).toUpperCase()}
              </div>
            </div>
          </div>
        </header>

        <div className="admin-content">
          {children}
        </div>
      </main>
    </div>
  )
}
