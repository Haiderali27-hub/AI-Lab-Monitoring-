import type { ReactNode } from 'react'
import { useAuth } from '../store/AuthContext'
import { useNavigate, useLocation, Link } from 'react-router-dom'

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

  const menuItems = [
    { label: 'Dashboard', icon: 'dashboard', path: '/admin' },
    { label: 'User Management', icon: 'group', path: '/admin/users' },
    { label: 'Exam Sessions', icon: 'security', path: '/admin/exams' },
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
          <Link to="/admin/settings" className={`nav-item ${location.pathname === '/admin/settings' ? 'active' : ''}`}>
            <span className="material-symbols-outlined">settings</span>
            <span>Settings</span>
          </Link>
        </nav>

        <div className="sidebar-footer">
          <button className="nav-item">
            <span className="material-symbols-outlined">help_center</span>
            <span>Support</span>
          </button>
          <button className="nav-item logout-btn" onClick={onLogout}>
            <span className="material-symbols-outlined">logout</span>
            <span>Logout</span>
          </button>
        </div>
      </aside>

      <main className="admin-main">
        <header className="admin-top-nav">
          <div className="top-nav-left">
            <div className="search-bar">
              <span className="material-symbols-outlined">search</span>
              <input type="text" placeholder="Search students, IDs, or departments..." />
            </div>
          </div>

          <div className="top-nav-right">
            <button className="icon-btn">
              <span className="material-symbols-outlined">notifications</span>
            </button>
            <button className="icon-btn">
              <span className="material-symbols-outlined">settings</span>
            </button>
            <div className="user-profile">
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
