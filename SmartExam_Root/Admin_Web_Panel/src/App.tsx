import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import type { UserRole } from './types';

// Pages
import LoginPage from './pages/auth/LoginPage';
import DashboardPage from './pages/admin/DashboardPage';
import UsersPage from './pages/admin/UsersPage';
import DeviceBindingsPage from './pages/admin/DeviceBindingsPage';
import LabsPage from './pages/admin/LabsPage';
import AuditLogsPage from './pages/admin/AuditLogsPage';
import TeacherDashboardPage from './pages/teacher/TeacherDashboardPage';
import CreateExamPage from './pages/teacher/CreateExamPage';
import LiveMonitorPage from './pages/teacher/LiveMonitorPage';
import ResultsPage from './pages/teacher/ResultsPage';
import EligibilityPage from './pages/teacher/EligibilityPage';

function PrivateRoute({ children, roles }: { children: React.ReactNode; roles: UserRole[] }) {
  const { user, isLoading } = useAuth();
  
  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }
  
  if (!user) {
    return <Navigate to="/login" replace />;
  }
  
  if (!roles.includes(user.role)) {
    return <Navigate to="/login" replace />;
  }
  
  return <>{children}</>;
}

function AppRoutes() {
  const { user } = useAuth();
  
  return (
    <Routes>
      <Route path="/login" element={
        user ? <Navigate to={user.role === 'Teacher' ? '/teacher/dashboard' : '/admin/dashboard'} replace /> : <LoginPage />
      } />

      {/* Admin */}
      <Route path="/admin/dashboard"      element={<PrivateRoute roles={['Admin','SuperAdmin']}><DashboardPage /></PrivateRoute>} />
      <Route path="/admin/users"          element={<PrivateRoute roles={['Admin','SuperAdmin']}><UsersPage /></PrivateRoute>} />
      <Route path="/admin/device-bindings" element={<PrivateRoute roles={['Admin','SuperAdmin']}><DeviceBindingsPage /></PrivateRoute>} />
      <Route path="/admin/labs"           element={<PrivateRoute roles={['Admin','SuperAdmin']}><LabsPage /></PrivateRoute>} />
      <Route path="/admin/audit-logs"     element={<PrivateRoute roles={['Admin','SuperAdmin']}><AuditLogsPage /></PrivateRoute>} />

      {/* Teacher */}
      <Route path="/teacher/dashboard"    element={<PrivateRoute roles={['Teacher']}><TeacherDashboardPage /></PrivateRoute>} />
      <Route path="/teacher/create-exam"  element={<PrivateRoute roles={['Teacher']}><CreateExamPage /></PrivateRoute>} />
      <Route path="/teacher/live-monitor" element={<PrivateRoute roles={['Teacher']}><LiveMonitorPage /></PrivateRoute>} />
      <Route path="/teacher/results/:examId" element={<PrivateRoute roles={['Teacher']}><ResultsPage /></PrivateRoute>} />
      <Route path="/teacher/eligibility"  element={<PrivateRoute roles={['Teacher']}><EligibilityPage /></PrivateRoute>} />

      <Route path="/" element={<Navigate to="/login" replace />} />
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </AuthProvider>
  );
}
