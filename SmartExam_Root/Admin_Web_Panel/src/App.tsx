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

// Analytics
import SystemAnalyticsPage from './pages/analytics/SystemAnalyticsPage';
import ExamAnalyticsPage from './pages/analytics/ExamAnalyticsPage';
import ReportsPage from './pages/analytics/ReportsPage';

// Notifications
import NotificationsPage from './pages/notifications/NotificationsPage';
import AnnouncementPage from './pages/notifications/AnnouncementPage';

// Student Portal
import StudentDashboardPage from './pages/student/StudentDashboardPage';
import StudentExamsPage from './pages/student/StudentExamsPage';
import StudentExamResultPage from './pages/student/StudentExamResultPage';
import StudentPerformancePage from './pages/student/StudentPerformancePage';
import StudentViolationsPage from './pages/student/StudentViolationsPage';
import StudentNotificationsPage from './pages/student/StudentNotificationsPage';

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
        user ? (
          <Navigate 
            to={
              user.role === 'Teacher' ? '/teacher/dashboard' :
              user.role === 'Student' ? '/student/dashboard' :
              '/admin/dashboard'
            } 
            replace 
          />
        ) : (
          <LoginPage />
        )
      } />

      {/* Admin */}
      <Route path="/admin/dashboard"      element={<PrivateRoute roles={['Admin','SuperAdmin']}><DashboardPage /></PrivateRoute>} />
      <Route path="/admin/users"          element={<PrivateRoute roles={['Admin','SuperAdmin']}><UsersPage /></PrivateRoute>} />
      <Route path="/admin/device-bindings" element={<PrivateRoute roles={['Admin','SuperAdmin']}><DeviceBindingsPage /></PrivateRoute>} />
      <Route path="/admin/labs"           element={<PrivateRoute roles={['Admin','SuperAdmin']}><LabsPage /></PrivateRoute>} />
      <Route path="/admin/audit-logs"     element={<PrivateRoute roles={['Admin','SuperAdmin']}><AuditLogsPage /></PrivateRoute>} />
      <Route path="/admin/analytics"      element={<PrivateRoute roles={['Admin','SuperAdmin']}><SystemAnalyticsPage /></PrivateRoute>} />
      <Route path="/admin/notifications"  element={<PrivateRoute roles={['Admin','SuperAdmin']}><NotificationsPage /></PrivateRoute>} />

      {/* Teacher */}
      <Route path="/teacher/dashboard"    element={<PrivateRoute roles={['Teacher']}><TeacherDashboardPage /></PrivateRoute>} />
      <Route path="/teacher/create-exam"  element={<PrivateRoute roles={['Teacher']}><CreateExamPage /></PrivateRoute>} />
      <Route path="/teacher/exams/:examId/edit" element={<PrivateRoute roles={['Teacher']}><CreateExamPage /></PrivateRoute>} />
      <Route path="/teacher/live-monitor" element={<PrivateRoute roles={['Teacher']}><LiveMonitorPage /></PrivateRoute>} />
      <Route path="/teacher/results/:examId" element={<PrivateRoute roles={['Teacher']}><ResultsPage /></PrivateRoute>} />
      <Route path="/teacher/results"      element={<PrivateRoute roles={['Teacher']}><ResultsPage /></PrivateRoute>} />
      <Route path="/teacher/eligibility"  element={<PrivateRoute roles={['Teacher']}><EligibilityPage /></PrivateRoute>} />
      <Route path="/teacher/analytics"    element={<PrivateRoute roles={['Teacher']}><ExamAnalyticsPage /></PrivateRoute>} />
      <Route path="/teacher/analytics/:examId" element={<PrivateRoute roles={['Teacher']}><ExamAnalyticsPage /></PrivateRoute>} />
      <Route path="/teacher/reports"      element={<PrivateRoute roles={['Teacher','Admin','SuperAdmin']}><ReportsPage /></PrivateRoute>} />
      <Route path="/teacher/notifications" element={<PrivateRoute roles={['Teacher']}><NotificationsPage /></PrivateRoute>} />
      <Route path="/teacher/announce"     element={<PrivateRoute roles={['Teacher']}><AnnouncementPage /></PrivateRoute>} />

      {/* Student Portal */}
      <Route path="/student/dashboard"    element={<PrivateRoute roles={['Student']}><StudentDashboardPage /></PrivateRoute>} />
      <Route path="/student/exams"        element={<PrivateRoute roles={['Student']}><StudentExamsPage /></PrivateRoute>} />
      <Route path="/student/exams/:examId/result" element={<PrivateRoute roles={['Student']}><StudentExamResultPage /></PrivateRoute>} />
      <Route path="/student/performance"  element={<PrivateRoute roles={['Student']}><StudentPerformancePage /></PrivateRoute>} />
      <Route path="/student/violations"   element={<PrivateRoute roles={['Student']}><StudentViolationsPage /></PrivateRoute>} />
      <Route path="/student/notifications" element={<PrivateRoute roles={['Student']}><StudentNotificationsPage /></PrivateRoute>} />

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
