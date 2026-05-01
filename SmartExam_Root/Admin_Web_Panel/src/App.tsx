import { Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AdminDashboard } from './pages/AdminDashboard'
import Exams from './pages/Exams'
import LabManagement from './pages/LabManagement'
import { LandingPage } from './pages/LandingPage'
import SetupPage from './pages/SetupPage'
import SuperAdminDashboard from './pages/SuperAdminDashboard'
import { destinationForRole } from './roleUtils'
import { useAuth } from './store/AuthContext'

function HomeRedirect() {
  const { isPlatformBootstrapped, isAuthenticated, user } = useAuth()

  if (isAuthenticated && user) {
    return <Navigate to={destinationForRole(user.role)} replace />
  }

  if (isPlatformBootstrapped === null) {
    return (
      <div className="auth-page" style={{ justifyContent: 'center', alignItems: 'center' }}>
        <p style={{ color: 'var(--primary)', fontWeight: 600 }}>Checking SmartExam setup...</p>
      </div>
    )
  }

  return <Navigate to="/login" replace />
}

function App() {
  return (
    <Routes>
      {/* Core routes */}
      <Route path="/" element={<HomeRedirect />} />
      <Route path="/setup" element={<SetupPage />} />
      <Route path="/login" element={<LandingPage />} />

      {/* ── Module 1: User Management (Dashboard) ── */}
      <Route
        path="/admin"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'Teacher', 'SuperAdmin']}>
            <AdminDashboard />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/users"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'SuperAdmin']}>
            <AdminDashboard />
          </ProtectedRoute>
        }
      />

      {/* ── Module 2: Exam Management ── */}
      <Route
        path="/admin/exams"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'Teacher']}>
            <Exams />
          </ProtectedRoute>
        }
      />

      {/* ── Module 3: Lab & Machine Management ── */}
      <Route
        path="/admin/labs"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'Teacher']}>
            <LabManagement />
          </ProtectedRoute>
        }
      />

      {/* ── SuperAdmin ── */}
      <Route
        path="/super-admin"
        element={
          <ProtectedRoute roles={['SuperAdmin']}>
            <SuperAdminDashboard />
          </ProtectedRoute>
        }
      />

      {/* ── Disabled / out-of-scope — redirect to home ── */}
      <Route path="/admin/settings" element={<Navigate to="/admin" replace />} />
      <Route path="/admin/logs"     element={<Navigate to="/admin" replace />} />
      <Route path="/admin/reports"  element={<Navigate to="/admin" replace />} />
      <Route path="/ui"             element={<Navigate to="/admin" replace />} />
      <Route path="/ui/:slug"       element={<Navigate to="/admin" replace />} />

      {/* Catch-all */}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export default App
