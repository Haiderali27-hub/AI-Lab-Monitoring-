import { Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AdminDashboard } from './pages/AdminDashboard'
import Exams from './pages/Exams'
import LabManagement from './pages/LabManagement'
import { LandingPage } from './pages/LandingPage'
import Monitoring from './pages/Monitoring'
import OrganizationSettings from './pages/OrganizationSettings'
import Reports from './pages/Reports'
import SetupPage from './pages/SetupPage'
import { StitchGallery } from './pages/StitchGallery'
import { StitchScreen } from './pages/StitchScreen'
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
      <Route path="/" element={<HomeRedirect />} />
      <Route path="/setup" element={<SetupPage />} />
      <Route path="/ui" element={<StitchGallery />} />
      <Route path="/ui/:slug" element={<StitchScreen />} />
      <Route path="/login" element={<LandingPage />} />
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
      <Route
        path="/admin/exams"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'Teacher']}>
            <Exams />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/labs"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'Teacher']}>
            <LabManagement />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/settings"
        element={
          <ProtectedRoute roles={['OrganizationAdmin']}>
            <OrganizationSettings />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/logs"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'Teacher']}>
            <Monitoring />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/reports"
        element={
          <ProtectedRoute roles={['OrganizationAdmin', 'Teacher']}>
            <Reports />
          </ProtectedRoute>
        }
      />
      <Route
        path="/super-admin"
        element={
          <ProtectedRoute roles={['SuperAdmin']}>
            <SuperAdminDashboard />
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export default App
