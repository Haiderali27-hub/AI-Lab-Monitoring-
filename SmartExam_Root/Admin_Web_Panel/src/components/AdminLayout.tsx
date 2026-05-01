import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { getOrganization, type OrganizationInfo } from '../api/admin'
import { formatRole, normalizeRole } from '../roleUtils'
import { useAuth } from '../store/AuthContext'

type AdminLayoutProps = {
  children: ReactNode
}

export function AdminLayout({ children }: AdminLayoutProps) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [organization, setOrganization] = useState<OrganizationInfo | null>(null)

  useEffect(() => {
    const fetchOrganization = async () => {
      try {
        const org = await getOrganization()
        setOrganization(org)
      } catch (error) {
        console.error('Failed to fetch organization:', error)
      }
    }

    if (user?.institutionId) {
      void fetchOrganization()
    }
  }, [user?.institutionId])

  async function onLogout() {
    await logout()
    navigate('/login')
  }

  const role = normalizeRole(user?.role)

  // ── Navigation: only the 3 core modules ─────────────────────────────────
  const menuItems = role === 'SuperAdmin'
    ? [
        { label: 'Institutions', icon: 'business_center', path: '/super-admin' },
      ]
    : role === 'OrganizationAdmin'
    ? [
        { label: 'Dashboard',        icon: 'dashboard',        path: '/admin' },
        { label: 'User Management',  icon: 'group',            path: '/admin/users' },
        { label: 'Exam Management',  icon: 'assignment',       path: '/admin/exams' },
        { label: 'Labs & Machines',  icon: 'desktop_windows',  path: '/admin/labs' },
      ]
    : // Teacher
      [
        { label: 'Dashboard',        icon: 'dashboard',        path: '/admin' },
        { label: 'Exam Management',  icon: 'assignment',       path: '/admin/exams' },
        { label: 'Labs & Machines',  icon: 'desktop_windows',  path: '/admin/labs' },
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
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
              <span className="status-badge secure">{formatRole(user?.role)}</span>
              {organization && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', paddingLeft: '12px', borderLeft: '1px solid var(--surface-alt)' }}>
                  <span className="material-symbols-outlined" style={{ fontSize: '18px', color: 'var(--on-surface-variant)' }}>business</span>
                  <div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--on-surface-variant)' }}>Organization</div>
                    <div style={{ fontWeight: 600, color: 'var(--neutral)' }}>{organization.name}</div>
                  </div>
                </div>
              )}
            </div>
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
