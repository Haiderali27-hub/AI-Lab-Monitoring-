# SmartExam — Modules 4, 5 & 6 Frontend Guide
### Analytics · Student Portal · Notifications
### Add on top of your existing React project

---

## Table of Contents

1. [Overview — What You Are Adding](#1-overview--what-you-are-adding)
2. [How to Get Screens from Stitch](#2-how-to-get-screens-from-stitch)
3. [New Files to Create](#3-new-files-to-create)
4. [New API Files](#4-new-api-files)
5. [New TypeScript Types](#5-new-typescript-types)
6. [Update App.tsx — New Routes](#6-update-apptsx--new-routes)
7. [Module 4 — Analytics Pages](#7-module-4--analytics-pages)
8. [Module 5 — Student Portal](#8-module-5--student-portal)
9. [Module 6 — Notifications](#9-module-6--notifications)
10. [Antigravity Prompts — One Per Screen](#10-antigravity-prompts--one-per-screen)
11. [Build Order](#11-build-order)
12. [Common Errors](#12-common-errors)

---

## 1. Overview — What You Are Adding

Your existing React project already has:
- ✅ Login page
- ✅ Admin pages (Dashboard, Users, Device Bindings, Labs, Audit Logs)
- ✅ Teacher pages (Dashboard, Create Exam, Live Monitor, Results, Eligibility)
- ✅ AuthContext, API client, all connection files

You are now adding:

| Module | New Pages | Where |
|--------|-----------|-------|
| **M4 Analytics** | System Analytics, Exam Analytics, Reports | Admin + Teacher sidebar |
| **M5 Student Portal** | Separate React app OR new routes with different layout | New portal |
| **M6 Notifications** | Bell dropdown, Notifications page, Announcement composer | All portals |

### Important decision — Student Portal

The student portal uses a **completely different layout** (top nav, no sidebar) from the admin/teacher panels. You have two options:

**Option A (Recommended — easier):** Add student routes to the same React project with a different layout component. Students get routed to `/student/...` paths with the `StudentLayout` instead of `AppLayout`.

**Option B:** Create a separate Vite project for the student portal.

**This guide uses Option A.** It is simpler, shares the same API client and types, and means you only run one dev server.

---

## 2. How to Get Screens from Stitch

You have 3 Stitch projects for these modules:
- "SmartExam — Analytics & Reports" (4 screens)
- "SmartExam — Student Portal" (5 screens)
- "SmartExam — Notifications" (6 screens)

Follow these steps for every screen before you start coding.

### Step 1 — Open the Stitch project
Go to the relevant Stitch project. You will see all generated screens as separate frames.

### Step 2 — Export each screen
Click each screen one at a time. Find the **Export** button (usually top right in Stitch). Export as **HTML/CSS**. Either:
- It downloads a ZIP — unzip and save the HTML file
- It shows code inline — copy and save it

### Step 3 — Save to your exports folder
Save each exported HTML file into `Admin_Web_Panel/stitch_exports/` using these exact names:

```
stitch_exports/
│
├── (existing files from modules 1-3...)
│
├── analytics_system.html           ← Screen 1 from Analytics project
├── analytics_exam_detail.html      ← Screen 2
├── analytics_reports.html          ← Screen 3
├── analytics_empty.html            ← Screen 4
│
├── student_dashboard.html          ← Screen 1 from Student Portal project
├── student_my_exams.html           ← Screen 2
├── student_exam_result.html        ← Screen 3
├── student_performance.html        ← Screen 4
├── student_violations.html         ← Screen 5
│
├── notif_bell_dropdown.html        ← Screen 1 from Notifications project
├── notif_admin_page.html           ← Screen 2
├── notif_announcement.html         ← Screen 3
├── notif_student_page.html         ← Screen 4
├── notif_empty.html                ← Screen 5
└── notif_toast.html                ← Screen 6
```

### Step 4 — What to take from the export
Same rule as before — use Stitch exports as **visual reference only**:
- ✅ Copy exact colors, spacing, border-radius values
- ✅ Use the HTML structure as a guide for your JSX layout
- ✅ Keep the Stitch tab open while coding in Antigravity
- ❌ Do NOT paste Stitch HTML directly into React files
- ❌ Do NOT keep hardcoded data from the design

---

## 3. New Files to Create

Run this from inside `Admin_Web_Panel/src/` to create all new folders and empty files at once:

```bash
# New page folders
mkdir -p pages/analytics pages/student pages/notifications

# Create empty page files
touch pages/analytics/SystemAnalyticsPage.tsx
touch pages/analytics/ExamAnalyticsPage.tsx
touch pages/analytics/ReportsPage.tsx
touch pages/student/StudentDashboardPage.tsx
touch pages/student/StudentExamsPage.tsx
touch pages/student/StudentExamResultPage.tsx
touch pages/student/StudentPerformancePage.tsx
touch pages/student/StudentViolationsPage.tsx
touch pages/student/StudentNotificationsPage.tsx
touch pages/notifications/NotificationsPage.tsx
touch pages/notifications/AnnouncementPage.tsx

# New layout component for student portal
touch components/layout/StudentLayout.tsx
touch components/layout/StudentNavbar.tsx

# New shared components
touch components/ui/Chart.tsx
touch components/ui/NotificationBell.tsx
touch components/ui/Toast.tsx
touch components/ui/ToastContainer.tsx

# New API files
touch api/analytics.api.ts
touch api/student.api.ts
touch api/notifications.api.ts

# New context for toasts
touch context/ToastContext.tsx
```

---

## 4. New API Files

Copy these exactly into your project.

---

### `src/api/analytics.api.ts`

```typescript
import apiClient from './client';

export const analyticsApi = {
  // GET /api/analytics/system
  // Admin only — system-wide stats and charts
  getSystemAnalytics: () =>
    apiClient.get('/analytics/system').then(r => r.data),

  // GET /api/analytics/exams/{examId}/summary
  // Teacher/Admin — single exam breakdown
  getExamSummary: (examId: string) =>
    apiClient.get(`/analytics/exams/${examId}/summary`).then(r => r.data),

  // GET /api/analytics/exams/{examId}/reports
  // List previously generated PDF reports for an exam
  getReportHistory: (examId: string) =>
    apiClient.get(`/analytics/exams/${examId}/reports`).then(r => r.data),

  // POST /api/analytics/exams/{examId}/report
  // Generate a new PDF report — returns downloadUrl
  generateReport: (examId: string) =>
    apiClient.post(`/analytics/exams/${examId}/report`).then(r => r.data),

  // GET /api/analytics/reports/{fileName}
  // Download a PDF — open in new tab
  getReportDownloadUrl: (fileName: string) =>
    `${apiClient.defaults.baseURL}/analytics/reports/${fileName}`,
};
```

---

### `src/api/student.api.ts`

```typescript
import apiClient from './client';

export const studentApi = {
  // GET /api/student/dashboard
  getDashboard: () =>
    apiClient.get('/student/dashboard').then(r => r.data),

  // GET /api/student/exams
  getMyExams: () =>
    apiClient.get('/student/exams').then(r => r.data),

  // GET /api/student/exams/{examId}/result
  getExamResult: (examId: string) =>
    apiClient.get(`/student/exams/${examId}/result`).then(r => r.data),

  // GET /api/student/violations
  getViolations: () =>
    apiClient.get('/student/violations').then(r => r.data),

  // GET /api/student/performance-trend
  getPerformanceTrend: () =>
    apiClient.get('/student/performance-trend').then(r => r.data),

  // GET /api/student/notifications
  getNotifications: () =>
    apiClient.get('/student/notifications').then(r => r.data),

  // PATCH /api/student/notifications/{id}/read
  markNotificationRead: (id: string) =>
    apiClient.patch(`/student/notifications/${id}/read`),
};
```

---

### `src/api/notifications.api.ts`

```typescript
import apiClient from './client';

export const notificationsApi = {
  // GET /api/notifications
  // Returns logged-in user's notifications + unread count
  getAll: () =>
    apiClient.get('/notifications').then(r => r.data),

  // PATCH /api/notifications/{id}/read
  markRead: (id: string) =>
    apiClient.patch(`/notifications/${id}/read`),

  // PATCH /api/notifications/read-all
  markAllRead: () =>
    apiClient.patch('/notifications/read-all').then(r => r.data),

  // POST /api/notifications/exam/{examId}/announce
  sendAnnouncement: (examId: string, data: {
    title: string;
    message: string;
    sendEmail: boolean;
  }) =>
    apiClient.post(`/notifications/exam/${examId}/announce`, data).then(r => r.data),

  // POST /api/notifications/exam/{examId}/notify-scheduled
  notifyScheduled: (examId: string) =>
    apiClient.post(`/notifications/exam/${examId}/notify-scheduled`).then(r => r.data),

  // POST /api/notifications/exam/{examId}/notify-grades
  notifyGrades: (examId: string) =>
    apiClient.post(`/notifications/exam/${examId}/notify-grades`).then(r => r.data),
};
```

---

## 5. New TypeScript Types

Open `src/types/index.ts` and ADD these at the bottom — do not remove existing types:

```typescript
// ── Analytics ─────────────────────────────────────────────────────────────────

export interface ExamSummaryAnalytics {
  examId: string;
  examTitle: string;
  totalStudents: number;
  totalMarks: number;
  averageScore: number;
  highestScore: number;
  lowestScore: number;
  passRate: number;
  scoreDistribution: Record<string, number>; // { "0-20": 3, "21-40": 5, ... }
  questionStats: QuestionStat[];
  totalViolations: number;
  studentsWithViolations: number;
}

export interface QuestionStat {
  questionId: string;
  shortLabel: string;    // "Q1", "Q2"
  fullText: string;      // truncated question text
  totalMarks: number;
  averageMarksEarned: number;
  difficultyPercent: number; // higher = easier (students scored well)
}

export interface SystemAnalytics {
  totalStudents: number;
  totalTeachers: number;
  totalExams: number;
  totalExamsLast30Days: number;
  totalViolationsLast30Days: number;
  examsByMonth: { label: string; count: number }[];
  topCoursesByExamCount: { label: string; count: number }[];
  violationTrendLast30Days: { date: string; count: number }[];
}

export interface ExamReport {
  reportId: string;
  reportType: string;
  generatedAt: string;
  fileName: string;
}

// ── Student Portal ─────────────────────────────────────────────────────────────

export interface StudentDashboard {
  totalExamsTaken: number;
  averageScore: number;
  examsPassed: number;
  totalViolations: number;
  recentExams: StudentExamSummary[];
  performanceTrend: { title: string; scorePercent: number; startedAt: string }[];
}

export interface StudentExamSummary {
  sessionId: string;
  examId: string;
  title: string;
  courseName: string;
  startedAt: string;
  submittedAt: string | null;
  status: string;
  totalMarks: number;
  earnedMarks: number;
  scorePercent: number;
  violationCount: number;
  passed: boolean;
}

export interface StudentExamResult {
  sessionId: string;
  title: string;
  startedAt: string;
  submittedAt: string | null;
  status: string;
  totalMarks: number;
  earnedMarks: number;
  scorePercent: number;
  passed: boolean;
  answers: StudentAnswer[];
}

export interface StudentAnswer {
  answerId: string;
  questionText: string;
  questionType: string;
  totalMarks: number;
  answerText: string;
  earnedMarks: number;
  aiFeedback: {
    suggestedMarks: number;
    justification: string;
    confidence: 'High' | 'Medium' | 'Low';
  } | null;
  teacherOverridden: boolean;
}

// ── Notifications ──────────────────────────────────────────────────────────────

export interface AppNotification {
  notificationId: string;
  title: string;
  body: string;
  type: string;
  isRead: boolean;
  createdAt: string;
  relatedEntityId: string | null;
}

export interface NotificationsResponse {
  unreadCount: number;
  notifications: AppNotification[];
}

// ── Toast ──────────────────────────────────────────────────────────────────────

export interface Toast {
  id: string;
  title: string;
  message: string;
  type: 'success' | 'warning' | 'info' | 'error';
}
```

---

## 6. Update App.tsx — New Routes

Open your existing `src/App.tsx` and add these imports and routes.

### Add these imports at the top:

```tsx
// Analytics
import SystemAnalyticsPage from './pages/analytics/SystemAnalyticsPage';
import ExamAnalyticsPage from './pages/analytics/ExamAnalyticsPage';
import ReportsPage from './pages/analytics/ReportsPage';

// Student Portal
import StudentLayout from './components/layout/StudentLayout';
import StudentDashboardPage from './pages/student/StudentDashboardPage';
import StudentExamsPage from './pages/student/StudentExamsPage';
import StudentExamResultPage from './pages/student/StudentExamResultPage';
import StudentPerformancePage from './pages/student/StudentPerformancePage';
import StudentViolationsPage from './pages/student/StudentViolationsPage';
import StudentNotificationsPage from './pages/student/StudentNotificationsPage';

// Notifications
import NotificationsPage from './pages/notifications/NotificationsPage';
import AnnouncementPage from './pages/notifications/AnnouncementPage';
```

### Add these routes inside your `<Routes>` block, after existing routes:

```tsx
{/* Analytics — Admin */}
<Route path="/admin/analytics" element={
  <PrivateRoute roles={['Admin','SuperAdmin']}>
    <SystemAnalyticsPage />
  </PrivateRoute>
} />

{/* Analytics — Teacher */}
<Route path="/teacher/analytics" element={
  <PrivateRoute roles={['Teacher']}>
    <ExamAnalyticsPage />
  </PrivateRoute>
} />
<Route path="/teacher/analytics/:examId" element={
  <PrivateRoute roles={['Teacher']}>
    <ExamAnalyticsPage />
  </PrivateRoute>
} />
<Route path="/teacher/reports" element={
  <PrivateRoute roles={['Teacher','Admin','SuperAdmin']}>
    <ReportsPage />
  </PrivateRoute>
} />

{/* Notifications */}
<Route path="/admin/notifications" element={
  <PrivateRoute roles={['Admin','SuperAdmin']}>
    <NotificationsPage />
  </PrivateRoute>
} />
<Route path="/teacher/notifications" element={
  <PrivateRoute roles={['Teacher']}>
    <NotificationsPage />
  </PrivateRoute>
} />
<Route path="/teacher/announce" element={
  <PrivateRoute roles={['Teacher']}>
    <AnnouncementPage />
  </PrivateRoute>
} />

{/* Student Portal — uses StudentLayout not AppLayout */}
<Route path="/student/dashboard" element={
  <PrivateRoute roles={['Student']}>
    <StudentDashboardPage />
  </PrivateRoute>
} />
<Route path="/student/exams" element={
  <PrivateRoute roles={['Student']}>
    <StudentExamsPage />
  </PrivateRoute>
} />
<Route path="/student/exams/:examId/result" element={
  <PrivateRoute roles={['Student']}>
    <StudentExamResultPage />
  </PrivateRoute>
} />
<Route path="/student/performance" element={
  <PrivateRoute roles={['Student']}>
    <StudentPerformancePage />
  </PrivateRoute>
} />
<Route path="/student/violations" element={
  <PrivateRoute roles={['Student']}>
    <StudentViolationsPage />
  </PrivateRoute>
} />
<Route path="/student/notifications" element={
  <PrivateRoute roles={['Student']}>
    <StudentNotificationsPage />
  </PrivateRoute>
} />
```

### Update the login redirect in App.tsx:

Find this line in your existing `AppRoutes` function:
```tsx
navigate(data.role === 'Teacher' ? '/teacher/dashboard' : '/admin/dashboard');
```

Replace with:
```tsx
if (data.role === 'Teacher') navigate('/teacher/dashboard');
else if (data.role === 'Student') navigate('/student/dashboard');
else navigate('/admin/dashboard');
```

### Update the default redirect for logged-in users:

Find this line:
```tsx
user ? <Navigate to={user.role === 'Teacher' ? '/teacher/dashboard' : '/admin/dashboard'} replace /> : <LoginPage />
```

Replace with:
```tsx
user
  ? <Navigate to={
      user.role === 'Teacher' ? '/teacher/dashboard' :
      user.role === 'Student' ? '/student/dashboard' :
      '/admin/dashboard'
    } replace />
  : <LoginPage />
```

---

### Update existing sidebars — add Analytics and Notifications links

Open `src/components/layout/AdminSidebar.tsx` and add:

```tsx
// Add to nav items array — after Device Bindings, before Audit Logs
{ label: 'Analytics',      icon: <BarChart2 size={18} />, path: '/admin/analytics' },
{ label: 'Notifications',  icon: <Bell size={18} />,      path: '/admin/notifications', badge: unreadCount },
```

Open `src/components/layout/TeacherSidebar.tsx` and add:

```tsx
// Add after Results & Grading, before Eligibility
{ label: 'Analytics',     icon: <BarChart2 size={18} />, path: '/teacher/analytics' },
{ label: 'Announce',      icon: <Megaphone size={18} />, path: '/teacher/announce' },
{ label: 'Notifications', icon: <Bell size={18} />,      path: '/teacher/notifications', badge: unreadCount },
```

To show the live unread count badge on the sidebar, add this to both sidebar components:

```tsx
import { notificationsApi } from '../../api/notifications.api';
import { useEffect, useState } from 'react';

// Inside the sidebar component
const [unreadCount, setUnreadCount] = useState(0);

useEffect(() => {
  notificationsApi.getAll()
    .then(data => setUnreadCount(data.unreadCount))
    .catch(() => {}); // silent fail — sidebar should never crash
}, []);
```

---

## 7. Module 4 — Analytics Pages

---

### `src/pages/analytics/SystemAnalyticsPage.tsx`

**What it shows:** System-wide stats for admins. 5 stat cards, bar chart (exams per month), horizontal bar chart (top courses), line chart (violation trend).

**API wiring:**

```typescript
import { analyticsApi } from '../../api/analytics.api';
import type { SystemAnalytics } from '../../types';
import AppLayout from '../../components/layout/AppLayout';

export default function SystemAnalyticsPage() {
  const [data, setData] = useState<SystemAnalytics | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analyticsApi.getSystemAnalytics()
      .then(setData)
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <AppLayout title="System Analytics"><LoadingSpinner /></AppLayout>;
  if (!data) return null;

  // Wire to UI:
  // Stat cards: data.totalStudents, data.totalTeachers, data.totalExams,
  //             data.totalExamsLast30Days, data.totalViolationsLast30Days

  // Bar chart "Exams Per Month":
  // data.examsByMonth → array of { label: "1/2025", count: 5 }
  // x-axis = label, bar height = count

  // Horizontal bar chart "Top Courses":
  // data.topCoursesByExamCount → array of { label: "CS301", count: 12 }
  // bar width = (count / max count) * 100 + "%"

  // Line chart "Violation Trend":
  // data.violationTrendLast30Days → array of { date: "2025-05-01", count: 3 }
}
```

**Chart rendering tip — no extra library needed for simple charts:**

For bar charts, render them as plain divs with dynamic widths — this matches the Stitch design perfectly and avoids adding recharts:

```tsx
// Simple horizontal bar chart
{data.topCoursesByExamCount.map((item, i) => {
  const maxCount = Math.max(...data.topCoursesByExamCount.map(x => x.count));
  const widthPct = maxCount > 0 ? (item.count / maxCount) * 100 : 0;
  return (
    <div key={i} className="flex items-center gap-3 mb-3">
      <span className="w-16 text-sm font-medium text-slate-700 text-right">{item.label}</span>
      <div className="flex-1 bg-slate-100 rounded-full h-6 overflow-hidden">
        <div
          className="h-full bg-primary-500 rounded-full transition-all duration-500"
          style={{ width: `${widthPct}%`, opacity: 0.4 + (widthPct / 100) * 0.6 }}
        />
      </div>
      <span className="w-8 text-sm text-slate-500">{item.count}</span>
    </div>
  );
})}

// Simple vertical bar chart
{data.examsByMonth.map((item, i) => {
  const maxCount = Math.max(...data.examsByMonth.map(x => x.count));
  const heightPct = maxCount > 0 ? (item.count / maxCount) * 100 : 0;
  return (
    <div key={i} className="flex flex-col items-center gap-1 flex-1">
      <span className="text-xs text-slate-500">{item.count}</span>
      <div className="w-full bg-slate-100 rounded-t-sm" style={{ height: '160px' }}>
        <div
          className="w-full bg-primary-500 rounded-t-sm mt-auto transition-all duration-500"
          style={{ height: `${heightPct}%`, marginTop: `${100 - heightPct}%` }}
        />
      </div>
      <span className="text-xs text-slate-400">{item.label}</span>
    </div>
  );
})}
```

---

### `src/pages/analytics/ExamAnalyticsPage.tsx`

**What it shows:** Per-exam analytics for teachers. Exam selector dropdown, 4 stat cards, score distribution bars, per-question difficulty bars, student leaderboard table.

**API wiring:**

```typescript
import { useParams } from 'react-router-dom';
import { analyticsApi } from '../../api/analytics.api';
import { examsApi } from '../../api/exams.api';
import type { ExamSummaryAnalytics, Exam } from '../../types';

export default function ExamAnalyticsPage() {
  const { examId: paramExamId } = useParams();
  const [exams, setExams] = useState<Exam[]>([]);
  const [selectedExamId, setSelectedExamId] = useState(paramExamId ?? '');
  const [analytics, setAnalytics] = useState<ExamSummaryAnalytics | null>(null);
  const [loading, setLoading] = useState(false);
  const [empty, setEmpty] = useState(!paramExamId);

  // Load exam dropdown
  useEffect(() => {
    examsApi.getAll().then(setExams);
  }, []);

  // Load analytics when exam is selected
  useEffect(() => {
    if (!selectedExamId) { setEmpty(true); return; }
    setLoading(true);
    setEmpty(false);
    analyticsApi.getExamSummary(selectedExamId)
      .then(setAnalytics)
      .finally(() => setLoading(false));
  }, [selectedExamId]);

  // Show empty state when no exam selected (Screen 4 from Stitch)
  if (empty) return <AppLayout title="Exam Analytics"><EmptyAnalyticsState /></AppLayout>;

  // Wire stat cards:
  // analytics.averageScore, analytics.passRate,
  // analytics.highestScore, analytics.totalViolations

  // Score distribution bars (5 buckets):
  // Object.entries(analytics.scoreDistribution).map(([label, count]) => ...)

  // Per-question difficulty bars:
  // analytics.questionStats.map(q => ...)
  // Color based on difficultyPercent: <50 = orange, >=50 = blue

  // Generate PDF button:
  const handleGenerateReport = async () => {
    const result = await analyticsApi.generateReport(selectedExamId);
    // Open PDF in new tab
    window.open(`http://localhost:5000${result.downloadUrl}`, '_blank');
  };
}
```

---

### `src/pages/analytics/ReportsPage.tsx`

**What it shows:** History of generated PDF reports. Table of past reports with download buttons.

**API wiring:**

```typescript
export default function ReportsPage() {
  const [exams, setExams] = useState<Exam[]>([]);
  const [selectedExamId, setSelectedExamId] = useState('');
  const [reports, setReports] = useState<ExamReport[]>([]);

  useEffect(() => { examsApi.getAll().then(setExams); }, []);

  useEffect(() => {
    if (!selectedExamId) return;
    analyticsApi.getReportHistory(selectedExamId).then(setReports);
  }, [selectedExamId]);

  const handleDownload = (fileName: string) => {
    const url = analyticsApi.getReportDownloadUrl(fileName);
    // Need to open with auth header — use fetch + blob trick
    fetch(url, {
      headers: { Authorization: `Bearer ${localStorage.getItem('smartexam_token')}` }
    })
    .then(r => r.blob())
    .then(blob => {
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = fileName;
      link.click();
    });
  };

  const handleGenerateNew = async () => {
    if (!selectedExamId) return;
    await analyticsApi.generateReport(selectedExamId);
    analyticsApi.getReportHistory(selectedExamId).then(setReports);
  };

  // Wire table:
  // reports.map(r => <tr key={r.reportId}>
  //   <td>{r.reportType}</td>
  //   <td>{new Date(r.generatedAt).toLocaleString()}</td>
  //   <td><button onClick={() => handleDownload(r.fileName)}>Download</button></td>
  // </tr>)
}
```

---

## 8. Module 5 — Student Portal

### `src/components/layout/StudentLayout.tsx`

This replaces `AppLayout` for all student pages. No sidebar — top nav only.

```tsx
import { ReactNode } from 'react';
import StudentNavbar from './StudentNavbar';

interface StudentLayoutProps {
  children: ReactNode;
}

export default function StudentLayout({ children }: StudentLayoutProps) {
  return (
    <div className="min-h-screen bg-slate-50">
      <StudentNavbar />
      <main className="max-w-6xl mx-auto px-6 py-8">
        {children}
      </main>
    </div>
  );
}
```

### `src/components/layout/StudentNavbar.tsx`

```tsx
import { NavLink, useNavigate } from 'react-router-dom';
import { Bell, LogOut, Shield } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { useState, useEffect } from 'react';
import { studentApi } from '../../api/student.api';

export default function StudentNavbar() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [unreadCount, setUnreadCount] = useState(0);

  useEffect(() => {
    studentApi.getNotifications()
      .then(data => setUnreadCount(data.unreadCount))
      .catch(() => {});
  }, []);

  const navLinks = [
    { label: 'Dashboard',    path: '/student/dashboard' },
    { label: 'My Exams',     path: '/student/exams' },
    { label: 'Performance',  path: '/student/performance' },
    { label: 'Violations',   path: '/student/violations' },
    { label: 'Notifications', path: '/student/notifications' },
  ];

  const initials = user?.name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2) ?? 'ST';

  return (
    <nav className="bg-white border-b border-slate-200 sticky top-0 z-40">
      <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between">
        {/* Logo */}
        <div className="flex items-center gap-2">
          <Shield size={22} className="text-primary-500" fill="currentColor" />
          <span className="font-bold text-slate-900">SmartExam</span>
          <span className="text-xs text-slate-400 ml-1">Student Portal</span>
        </div>

        {/* Nav links */}
        <div className="flex items-center gap-1">
          {navLinks.map(link => (
            <NavLink
              key={link.path}
              to={link.path}
              className={({ isActive }) =>
                `px-4 py-2 text-sm rounded-md transition-colors relative
                ${isActive
                  ? 'text-primary-600 font-medium bg-primary-50'
                  : 'text-slate-500 hover:text-slate-700 hover:bg-slate-50'
                }`
              }
            >
              {link.label}
              {link.label === 'Notifications' && unreadCount > 0 && (
                <span className="absolute -top-1 -right-1 bg-red-500 text-white text-xs rounded-full w-4 h-4 flex items-center justify-center">
                  {unreadCount}
                </span>
              )}
            </NavLink>
          ))}
        </div>

        {/* Right side */}
        <div className="flex items-center gap-3">
          <button
            onClick={() => navigate('/student/notifications')}
            className="relative p-2 text-slate-400 hover:text-slate-600"
          >
            <Bell size={18} />
            {unreadCount > 0 && (
              <span className="absolute top-1 right-1 w-2 h-2 bg-red-500 rounded-full" />
            )}
          </button>
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-full bg-primary-500 text-white text-xs font-bold flex items-center justify-center">
              {initials}
            </div>
            <span className="text-sm font-medium text-slate-700">{user?.name}</span>
          </div>
          <button onClick={logout} className="p-2 text-slate-400 hover:text-red-500 transition-colors">
            <LogOut size={16} />
          </button>
        </div>
      </div>
    </nav>
  );
}
```

---

### `src/pages/student/StudentDashboardPage.tsx`

**API wiring:**

```typescript
import { studentApi } from '../../api/student.api';
import StudentLayout from '../../components/layout/StudentLayout';
import type { StudentDashboard } from '../../types';
import { useAuth } from '../../context/AuthContext';

export default function StudentDashboardPage() {
  const { user } = useAuth();
  const [data, setData] = useState<StudentDashboard | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    studentApi.getDashboard()
      .then(setData)
      .finally(() => setLoading(false));
  }, []);

  // Wire greeting: "Welcome back, {user?.name}"
  // Wire stat cards: data.totalExamsTaken, data.averageScore, data.totalViolations
  // Wire recent exams list: data.recentExams.map(exam => ...)
  // Wire performance trend line chart: data.performanceTrend
  //   x = exam title, y = scorePercent
  //   Color dot green if scorePercent >= 50, red if < 50

  // Score color helper:
  const scoreColor = (pct: number) => pct >= 50 ? 'text-green-600' : 'text-red-500';
  const scoreBg = (pct: number) => pct >= 50 ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-700';
}
```

---

### `src/pages/student/StudentExamsPage.tsx`

**API wiring:**

```typescript
export default function StudentExamsPage() {
  const navigate = useNavigate();
  const [exams, setExams] = useState<any[]>([]);
  const [filter, setFilter] = useState<'All' | 'Passed' | 'Failed' | 'Upcoming'>('All');

  useEffect(() => {
    studentApi.getMyExams().then(setExams);
  }, []);

  const filtered = exams.filter(e => {
    if (filter === 'All') return true;
    if (filter === 'Passed') return e.status === 'Ended' && e.scorePercent >= 50;
    if (filter === 'Failed') return e.status === 'Ended' && e.scorePercent < 50;
    if (filter === 'Upcoming') return e.status === 'Scheduled' || e.status === 'Active';
    return true;
  });

  // Wire filter pills: active pill = blue bg + white text
  // Wire exam cards grid: filtered.map(exam => <ExamCard key={exam.examId} exam={exam} />)
  // "View Detailed Results" button: navigate(`/student/exams/${exam.examId}/result`)
  // Only show results button when exam.status === 'Ended'
}
```

---

### `src/pages/student/StudentExamResultPage.tsx`

**API wiring:**

```typescript
export default function StudentExamResultPage() {
  const { examId } = useParams();
  const navigate = useNavigate();
  const [result, setResult] = useState<StudentExamResult | null>(null);
  const [activeTab, setActiveTab] = useState<'answers' | 'feedback' | 'violations'>('answers');

  useEffect(() => {
    if (!examId) return;
    studentApi.getExamResult(examId).then(setResult);
  }, [examId]);

  // Wire header:
  // result.title, result.scorePercent (large, colored),
  // result.passed → "Passed" green badge or "Failed" red badge
  // result.earnedMarks / result.totalMarks

  // Wire "My Answers" tab:
  // result.answers.map(a => ...)
  // For coding: dark code block <pre className="bg-slate-900 text-green-400 p-4 rounded-lg text-sm font-mono overflow-x-auto">{a.answerText}</pre>
  // For theory: <div className="bg-slate-50 border-l-4 border-primary-500 p-4 text-slate-700">{a.answerText}</div>
  // Show AI confidence badge color: High=green, Medium=orange, Low=red
  // If teacherOverridden: show "Teacher Reviewed ✓" orange badge instead of AI badge

  // Wire "AI Feedback" tab:
  // Same answers but focus on justification text
  // Progress bar: width = (a.earnedMarks / a.totalMarks) * 100 + "%"
}
```

---

### `src/pages/student/StudentPerformancePage.tsx`

**API wiring:**

```typescript
export default function StudentPerformancePage() {
  const [trend, setTrend] = useState<any[]>([]);
  const [dashboard, setDashboard] = useState<StudentDashboard | null>(null);

  useEffect(() => {
    Promise.all([
      studentApi.getPerformanceTrend(),
      studentApi.getDashboard()
    ]).then(([trendData, dashData]) => {
      setTrend(trendData);
      setDashboard(dashData);
    });
  }, []);

  // Wire stat cards (soft colored backgrounds from Stitch design):
  // dashboard.totalExamsTaken → blue card
  // dashboard.averageScore    → green card
  // Best score = Math.max(...trend.map(t => t.scorePercent)) → green card
  // dashboard.totalViolations → orange card

  // Wire "Score Over Time" line chart:
  // trend.map(t => ({ x: t.examTitle, y: t.scorePercent }))
  // Pass mark line at y=50 — rendered as a dashed div

  // Simple CSS line chart (no library):
  // Plot points as absolute-positioned dots in a relative container
  // Connect with SVG polyline
  const chartPoints = trend.map((t, i) => ({
    x: (i / Math.max(trend.length - 1, 1)) * 100,
    y: 100 - t.scorePercent,
    passed: t.scorePercent >= 50,
    label: t.examTitle
  }));
}
```

---

### `src/pages/student/StudentViolationsPage.tsx`

**API wiring:**

```typescript
export default function StudentViolationsPage() {
  const [data, setData] = useState<{ totalViolations: number; violations: any[] } | null>(null);

  useEffect(() => {
    studentApi.getViolations().then(setData);
  }, []);

  // If data.totalViolations === 0: show the empty/success state from Stitch Screen 5
  // (green checkmark, "No violations recorded", "Keep it up!")

  // If violations exist: show the info banner then violation cards
  // Each card: violation type badge + exam title + date + payload details
  // Parse payload JSON: const details = JSON.parse(violation.payload)
  // Show details.activeWindow or details.blockedApp from the payload
}
```

---

### `src/pages/student/StudentNotificationsPage.tsx`

**API wiring:**

```typescript
export default function StudentNotificationsPage() {
  const [data, setData] = useState<NotificationsResponse | null>(null);

  useEffect(() => {
    studentApi.getNotifications().then(setData);
  }, []);

  const handleMarkRead = async (id: string) => {
    await studentApi.markNotificationRead(id);
    // Update local state immediately
    setData(prev => prev ? {
      ...prev,
      unreadCount: Math.max(0, prev.unreadCount - 1),
      notifications: prev.notifications.map(n =>
        n.notificationId === id ? { ...n, isRead: true } : n
      )
    } : prev);
  };

  // If data.notifications.length === 0: show Screen 5 empty state from Stitch
  // (bell with checkmark icon, "You're all caught up!")

  // Notification type → icon color:
  const typeColor = (type: string) => ({
    'ExamScheduled':    'bg-blue-100 text-blue-600',
    'GradeReleased':    'bg-green-100 text-green-600',
    'ViolationWarning': 'bg-orange-100 text-orange-600',
    'EligibilityChanged': 'bg-yellow-100 text-yellow-600',
    'Announcement':     'bg-purple-100 text-purple-600',
    'ExamReminder':     'bg-blue-100 text-blue-600',
  })[type] ?? 'bg-slate-100 text-slate-600';
}
```

---

## 9. Module 6 — Notifications

### `src/context/ToastContext.tsx`

Toasts appear from any page without props drilling. Wire this once and use it everywhere.

```tsx
import { createContext, useContext, useState, useCallback, ReactNode } from 'react';
import type { Toast } from '../types';

interface ToastContextType {
  toasts: Toast[];
  showToast: (title: string, message: string, type: Toast['type']) => void;
  removeToast: (id: string) => void;
}

const ToastContext = createContext<ToastContextType | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const showToast = useCallback((title: string, message: string, type: Toast['type']) => {
    const id = Date.now().toString();
    setToasts(prev => [{ id, title, message, type }, ...prev]);
    // Auto-remove after 5 seconds
    setTimeout(() => removeToast(id), 5000);
  }, []);

  const removeToast = useCallback((id: string) => {
    setToasts(prev => prev.filter(t => t.id !== id));
  }, []);

  return (
    <ToastContext.Provider value={{ toasts, showToast, removeToast }}>
      {children}
    </ToastContext.Provider>
  );
}

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be inside ToastProvider');
  return ctx;
}
```

Wrap `ToastProvider` around your app in `main.tsx`:

```tsx
// src/main.tsx
import { ToastProvider } from './context/ToastContext';
import ToastContainer from './components/ui/ToastContainer';

createRoot(document.getElementById('root')!).render(
  <ToastProvider>
    <App />
    <ToastContainer />
  </ToastProvider>
);
```

---

### `src/components/ui/ToastContainer.tsx`

```tsx
import { X } from 'lucide-react';
import { useToast } from '../../context/ToastContext';

const typeStyles = {
  success: { bar: 'bg-green-500', icon: '✓', ring: 'bg-green-100 text-green-700' },
  warning: { bar: 'bg-orange-500', icon: '⚠', ring: 'bg-orange-100 text-orange-700' },
  info:    { bar: 'bg-blue-500',   icon: 'ℹ', ring: 'bg-blue-100 text-blue-700' },
  error:   { bar: 'bg-red-500',    icon: '✕', ring: 'bg-red-100 text-red-700' },
};

export default function ToastContainer() {
  const { toasts, removeToast } = useToast();

  return (
    <div className="fixed bottom-6 right-6 z-50 flex flex-col gap-2 w-80">
      {toasts.map(toast => {
        const style = typeStyles[toast.type];
        return (
          <div key={toast.id} className="bg-white rounded-lg border border-slate-200 shadow-lg overflow-hidden animate-in slide-in-from-right-5">
            <div className="p-4 flex items-start gap-3">
              <span className={`w-6 h-6 rounded-full flex items-center justify-center text-xs flex-shrink-0 ${style.ring}`}>
                {style.icon}
              </span>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-slate-900">{toast.title}</p>
                <p className="text-xs text-slate-500 mt-0.5">{toast.message}</p>
              </div>
              <button onClick={() => removeToast(toast.id)} className="text-slate-300 hover:text-slate-500">
                <X size={14} />
              </button>
            </div>
            {/* Progress bar */}
            <div className={`h-1 ${style.bar} animate-shrink`} />
          </div>
        );
      })}
    </div>
  );
}
```

Add this animation to `index.css`:
```css
@keyframes shrink {
  from { width: 100%; }
  to { width: 0%; }
}
.animate-shrink {
  animation: shrink 5s linear forwards;
}
```

---

### `src/components/ui/NotificationBell.tsx`

This is the bell icon in the admin/teacher navbar that opens the dropdown panel.

```tsx
import { useState, useEffect, useRef } from 'react';
import { Bell, X } from 'lucide-react';
import { notificationsApi } from '../../api/notifications.api';
import type { AppNotification, NotificationsResponse } from '../../types';
import { formatDistanceToNow } from 'date-fns';

const typeColor = (type: string) => ({
  'ExamScheduled':    'bg-blue-500',
  'GradeReleased':    'bg-green-500',
  'ViolationWarning': 'bg-red-500',
  'EligibilityChanged': 'bg-orange-500',
  'Announcement':     'bg-purple-500',
})[type] ?? 'bg-slate-400';

export default function NotificationBell() {
  const [open, setOpen] = useState(false);
  const [data, setData] = useState<NotificationsResponse | null>(null);
  const ref = useRef<HTMLDivElement>(null);

  const loadNotifications = () => {
    notificationsApi.getAll().then(setData).catch(() => {});
  };

  useEffect(() => { loadNotifications(); }, []);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  const handleMarkAll = async () => {
    await notificationsApi.markAllRead();
    loadNotifications();
  };

  const handleMarkOne = async (id: string) => {
    await notificationsApi.markRead(id);
    loadNotifications();
  };

  return (
    <div className="relative" ref={ref}>
      {/* Bell button */}
      <button
        onClick={() => setOpen(o => !o)}
        className="relative p-2 text-slate-400 hover:text-slate-600 transition-colors"
      >
        <Bell size={20} />
        {data && data.unreadCount > 0 && (
          <span className="absolute top-1 right-1 w-4 h-4 bg-red-500 text-white text-xs rounded-full flex items-center justify-center font-bold">
            {data.unreadCount > 9 ? '9+' : data.unreadCount}
          </span>
        )}
      </button>

      {/* Dropdown panel */}
      {open && (
        <div className="absolute right-0 top-full mt-2 w-96 bg-white border border-slate-200 rounded-lg shadow-xl z-50">
          {/* Header */}
          <div className="flex items-center justify-between px-4 py-3 border-b border-slate-100">
            <span className="font-semibold text-slate-900 text-sm">Notifications</span>
            <button onClick={handleMarkAll} className="text-xs text-primary-600 hover:text-primary-700 font-medium">
              Mark all read
            </button>
          </div>

          {/* List */}
          <div className="max-h-96 overflow-y-auto">
            {!data || data.notifications.length === 0 ? (
              <div className="py-12 text-center text-slate-400 text-sm">No notifications</div>
            ) : (
              data.notifications.slice(0, 8).map(n => (
                <div
                  key={n.notificationId}
                  onClick={() => !n.isRead && handleMarkOne(n.notificationId)}
                  className={`flex gap-3 px-4 py-3 border-b border-slate-50 cursor-pointer hover:bg-slate-50 transition-colors
                    ${!n.isRead ? 'bg-blue-50/40' : ''}`}
                >
                  <span className={`w-3 h-3 rounded-full mt-1 flex-shrink-0 ${typeColor(n.type)}`} />
                  <div className="flex-1 min-w-0">
                    <p className={`text-sm ${!n.isRead ? 'font-semibold text-slate-900' : 'font-medium text-slate-700'}`}>
                      {n.title}
                    </p>
                    <p className="text-xs text-slate-500 mt-0.5 line-clamp-2">{n.body}</p>
                    <p className="text-xs text-slate-400 mt-1">
                      {formatDistanceToNow(new Date(n.createdAt), { addSuffix: true })}
                    </p>
                  </div>
                  {!n.isRead && <span className="w-2 h-2 bg-primary-500 rounded-full flex-shrink-0 mt-2" />}
                </div>
              ))
            )}
          </div>

          {/* Footer */}
          <div className="px-4 py-2 border-t border-slate-100 text-center">
            <a href="/admin/notifications" className="text-xs text-primary-600 hover:text-primary-700">
              View all notifications →
            </a>
          </div>
        </div>
      )}
    </div>
  );
}
```

Add `<NotificationBell />` to your existing `Navbar.tsx` — replace the static bell icon with this component.

---

### `src/pages/notifications/NotificationsPage.tsx`

**API wiring:**

```typescript
export default function NotificationsPage() {
  const [data, setData] = useState<NotificationsResponse | null>(null);
  const [filter, setFilter] = useState<'All'|'Unread'|'Exam'|'Violations'|'System'>('All');

  const load = () => notificationsApi.getAll().then(setData);
  useEffect(() => { load(); }, []);

  const handleMarkAll = async () => {
    await notificationsApi.markAllRead();
    load();
  };

  const filtered = (data?.notifications ?? []).filter(n => {
    if (filter === 'All') return true;
    if (filter === 'Unread') return !n.isRead;
    if (filter === 'Violations') return n.type === 'ViolationWarning';
    if (filter === 'Exam') return ['ExamScheduled','GradeReleased','ExamReminder'].includes(n.type);
    if (filter === 'System') return ['EligibilityChanged','Announcement'].includes(n.type);
    return true;
  });

  // Wire tab pills: filter state controls which tab is active
  // Wire notification cards: filtered.map(n => <NotificationCard key={n.notificationId} n={n} />)
  // Unread card: blue left border + blue tint background
  // Read card: no border accent, normal white background
}
```

---

### `src/pages/notifications/AnnouncementPage.tsx`

**API wiring:**

```typescript
export default function AnnouncementPage() {
  const { showToast } = useToast();
  const [exams, setExams] = useState<Exam[]>([]);
  const [selectedExamId, setSelectedExamId] = useState('');
  const [title, setTitle] = useState('');
  const [message, setMessage] = useState('');
  const [sendEmail, setSendEmail] = useState(false);
  const [loading, setLoading] = useState(false);
  const [recipientCount, setRecipientCount] = useState(0);

  useEffect(() => { examsApi.getAll().then(setExams); }, []);

  // When exam changes, get assignment count for recipient preview
  useEffect(() => {
    if (!selectedExamId) { setRecipientCount(0); return; }
    examsApi.getAssignments(selectedExamId)
      .then(a => setRecipientCount(a.filter(s => s.isEligible).length));
  }, [selectedExamId]);

  const handleSend = async () => {
    if (!selectedExamId || !title.trim() || !message.trim()) return;
    setLoading(true);
    try {
      const result = await notificationsApi.sendAnnouncement(selectedExamId, {
        title, message, sendEmail
      });
      showToast('Announcement Sent', result.message, 'success');
      setTitle(''); setMessage(''); setSendEmail(false);
    } catch {
      showToast('Failed', 'Could not send announcement.', 'error');
    } finally {
      setLoading(false);
    }
  };

  // Wire exam dropdown: exams.map(e => <option value={e.examId}>{e.title}</option>)
  // Wire recipient preview: "This will be sent to {recipientCount} eligible students"
  // Show email warning banner when sendEmail === true
  // Wire send button: onClick={handleSend} disabled={loading || !selectedExamId || !title || !message}
}
```

---

## 10. Antigravity Prompts — One Per Screen

Use these in Antigravity. Open the relevant Stitch HTML export, then paste the prompt followed by the HTML.

---

### System Analytics Page
```
Convert this Stitch HTML into src/pages/analytics/SystemAnalyticsPage.tsx

Rules:
- Import AppLayout with title="System Analytics"
- The 5 stat cards accept data prop not hardcoded values
- Bar chart "Exams Per Month": replace hardcoded bars with a .map() over an examsByMonth array prop
  Each bar height is dynamic: style={{ height: `${(item.count / maxCount) * 100}%` }}
- Horizontal bar "Top Courses": replace with .map() over topCourses array
  Each bar width: style={{ width: `${(item.count / maxCount) * 100}%` }}
- Line chart "Violation Trend": render as SVG polyline or CSS dots — map over violationTrend array
- Date range selector uses useState selectedRange
- All data comes from props, no hardcoded numbers
- Use Tailwind, no inline styles except dynamic widths/heights

[paste analytics_system.html here]
```

---

### Exam Analytics Page
```
Convert this Stitch HTML into src/pages/analytics/ExamAnalyticsPage.tsx

Rules:
- Import AppLayout with title="Exam Analytics"
- Exam selector dropdown uses useState selectedExamId
- Show the empty state component (no exam selected) when selectedExamId is empty
- 4 stat cards accept data props
- Score distribution bars: 5 bars, each width from scoreDistribution object values
  Colors: 0-20=red, 21-40=orange, 41-60=yellow, 61-80=lightblue, 81-100=blue
- Per-question difficulty bars: map over questionStats array
  Bar color: orange if difficultyPercent < 50, blue if >= 50
- "Generate PDF Report" button calls an empty handleGenerateReport function
- Student leaderboard: table rows from .map() with rank, name, score, violations
- Top 3 rows have a gold/silver/bronze left border: rank 1=yellow, rank 2=slate, rank 3=orange

[paste analytics_exam_detail.html here]
```

---

### Reports Page
```
Convert this Stitch HTML into src/pages/analytics/ReportsPage.tsx

Rules:
- Import AppLayout with title="Generated Reports"
- Exam selector dropdown uses useState selectedExamId
- Reports table rows from .map() over reports array
- Each row: reportType badge, generatedAt formatted date, download button
- "Generate New Report" button calls handleGenerateNew()
- Download button calls handleDownload(fileName)
- Report preview section below table is static/decorative (no interactivity needed)
- Empty state shown when reports array is empty

[paste analytics_reports.html here]
```

---

### Student Dashboard
```
Convert this Stitch HTML into src/pages/student/StudentDashboardPage.tsx

Rules:
- Import StudentLayout (NOT AppLayout — this is the student portal)
- Greeting card uses user.name from useAuth() hook
- The 3 summary stat chips in the greeting card: totalExamsTaken, averageScore, totalViolations
- 3 main stat cards accept data props with soft colored top borders
- Recent exams list: data.recentExams.slice(0,4).map(exam => ...)
  Score color: green if passed, red if failed
  Date formatted with date-fns: format(new Date(exam.startedAt), 'MMM dd, yyyy')
- Performance trend chart: data.performanceTrend.map(...)
  Simple CSS-based line chart or dots — no chart library
- Upcoming exams chips: filter exams where status === 'Scheduled'
- Empty state for upcoming exams when none exist

[paste student_dashboard.html here]
```

---

### Student My Exams
```
Convert this Stitch HTML into src/pages/student/StudentExamsPage.tsx

Rules:
- Import StudentLayout
- Filter pills (All/Passed/Failed/Upcoming) use useState filter
  Active pill: blue background white text. Inactive: white background gray text.
- Exam cards grid: filtered.map(exam => card)
  Course code badge: first 3 chars of course code
  Status badge colors: Passed=green, Failed=red, Scheduled=blue, Active=green pulsing
  Score shown large and colored when exam is ended
  "View Detailed Results" button only shown when exam.status === 'Ended'
  navigate to /student/exams/{examId}/result on click
- Cards use 2-column grid on desktop, 1-column on mobile

[paste student_my_exams.html here]
```

---

### Student Exam Result
```
Convert this Stitch HTML into src/pages/student/StudentExamResultPage.tsx

Rules:
- Import StudentLayout
- Back button: navigate(-1) on click
- Result header: score percentage large (48px), colored green if passed red if failed
- Tab bar (My Answers / AI Feedback Summary / Violations) uses useState activeTab
- "My Answers" tab:
  Each answer card has questionType badge
  Coding answers: dark code block with monospace font bg-slate-900 text-green-400
  Theory answers: white block with left blue border
  Marks earned shown at bottom of each card
  AI confidence badge: High=green, Medium=orange, Low=red
  If teacherOverridden=true: show orange "Teacher Reviewed ✓" badge instead
- "AI Feedback" tab:
  Progress bar width: (earnedMarks / totalMarks * 100) + "%"
  Justification in italic blockquote style with left blue border
- All data from result state, no hardcoded text

[paste student_exam_result.html here]
```

---

### Student Performance
```
Convert this Stitch HTML into src/pages/student/StudentPerformancePage.tsx

Rules:
- Import StudentLayout
- 4 stat cards use soft colored backgrounds (not white):
  blue bg for totalExamsTaken, green bg for averageScore and bestScore, orange bg for violations
- "Score Over Time" line chart: trend.map() to plot dots and line
  Pass mark dotted line at 50% height
  Dots colored: green if scorePercent >= 50, red if < 50
  If trend is improving (last > first): show "Improving ↑" green chip top right of card
- "Performance by Course" horizontal bars: group trend data by course
- "Exam Timeline": trend items as vertical list with colored dots and connecting line

[paste student_performance.html here]
```

---

### Student Violations
```
Convert this Stitch HTML into src/pages/student/StudentViolationsPage.tsx

Rules:
- Import StudentLayout
- If totalViolations === 0: show empty/success state ONLY (green checkmark, "No violations recorded")
- If violations exist:
  Show info banner (soft orange background, not red — friendly tone)
  Show summary chips: total count + last violation date
  Violation cards: map over violations array
  Violation type badge derived from payload: parse JSON payload to get blocked app name
  Date formatted nicely with date-fns
  Details code block: small monospace block showing raw violation info

[paste student_violations.html here]
```

---

### Notifications Page (Admin/Teacher)
```
Convert this Stitch HTML into src/pages/notifications/NotificationsPage.tsx

Rules:
- Import AppLayout with title="Notifications"
- Filter tabs (All/Unread/Exam/Violations/System) use useState filter, underline style
- "Mark all as read" button calls handleMarkAll()
- Notification cards: filtered.map(n => card)
  Unread cards: left border 3px blue + very subtle blue-50 background
  Read cards: normal white, no border accent
  Type color circles match notification type
  Time displayed with date-fns formatDistanceToNow
  relatedEntityId shown as a link if not null
- Pagination shows total count

[paste notif_admin_page.html here]
```

---

### Announcement Page
```
Convert this Stitch HTML into src/pages/notifications/AnnouncementPage.tsx

Rules:
- Import AppLayout with title="Send Announcement"
- Exam dropdown: exams.map(e => option)
- Recipient preview box: shows recipientCount — updates when exam changes
- Title input: useState title
- Message textarea: useState message
- Email toggle: useState sendEmail boolean
- Email warning banner: only visible when sendEmail === true
- Send button: disabled when loading or fields empty
  Shows loading spinner when loading === true
- On success: call showToast from useToast() hook

[paste notif_announcement.html here]
```

---

### Student Notifications
```
Convert this Stitch HTML into src/pages/student/StudentNotificationsPage.tsx

Rules:
- Import StudentLayout
- "Mark all as read" text link top right
- If notifications.length === 0: show empty state ONLY (bell icon, "You're all caught up!")
- Notification cards: data.notifications.map(n => card)
  Card style follows student portal — 12px radius, more padding, friendly tone
  Unread: soft blue left border + blue-50/40 background tint
  Read: normal white
  Type color from typeColor() helper function
  "View exam results →" link shown when relatedEntityId exists and type is GradeReleased
  Click on unread card: call handleMarkRead(n.notificationId)

[paste notif_student_page.html here]
```

---

## 11. Build Order

Follow this sequence. Each step should be working before the next.

```
WEEK 1 — Notifications (quickest win, visible in all portals)

  □ Copy api/notifications.api.ts into project
  □ Add ToastContext + ToastContainer to main.tsx
  □ Build NotificationBell component
  □ Replace static bell icon in existing Navbar.tsx with NotificationBell
  □ Build NotificationsPage (admin/teacher)
  □ Add notification routes to App.tsx
  □ Add "Notifications" link to both sidebars with unread badge
  ✓ CHECKPOINT: Bell shows unread count, dropdown opens, notifications page loads

  □ Build AnnouncementPage
  □ Add "Announce" link to teacher sidebar
  ✓ CHECKPOINT: Teacher can send announcement, toast appears, students receive it

WEEK 2 — Analytics

  □ Copy api/analytics.api.ts into project
  □ Add analytics types to types/index.ts
  □ Build SystemAnalyticsPage (admin)
  □ Add "Analytics" to admin sidebar
  ✓ CHECKPOINT: Admin sees real system stats and charts

  □ Build ExamAnalyticsPage with empty state (teacher)
  □ Add "Analytics" to teacher sidebar
  □ Build ReportsPage
  □ Wire "Generate PDF" button — PDF opens in new tab
  ✓ CHECKPOINT: Teacher selects exam, sees charts, downloads PDF

WEEK 3 — Student Portal

  □ Copy api/student.api.ts into project
  □ Build StudentLayout + StudentNavbar
  □ Update login redirect for Student role
  □ Build StudentDashboardPage
  ✓ CHECKPOINT: Student logs in, lands on dashboard, sees real data

  □ Build StudentExamsPage with filter pills
  □ Build StudentExamResultPage (answers + AI feedback tabs)
  □ Build StudentPerformancePage (line chart)
  □ Build StudentViolationsPage (empty state + violation cards)
  □ Build StudentNotificationsPage
  ✓ CHECKPOINT: Full student portal works end-to-end

STRETCH (if time)
  □ Add SVG line chart to performance page (more polished)
  □ Notification polling every 60 seconds (auto-refresh unread count)
  □ Analytics date range filter (last 7/30/90 days)
```

---

## 12. Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `403` on `/api/analytics/system` with teacher token | Teacher role blocked from system analytics | Use admin token for system analytics, teacher token for exam analytics |
| `403` on `/api/student/...` with admin token | Student portal endpoints are Student-only | Log in as the student seed account to test these |
| PDF download shows empty/garbled file | Auth header not sent with file request | Use the fetch+blob trick in `handleDownload` — do not use `<a href>` directly |
| `404` on `/api/analytics/reports/{fileName}` | Reports folder doesn't exist | Make sure `Directory.CreateDirectory("Reports/")` runs in the service — it is already in the backend code |
| Bell dropdown doesn't close on outside click | `useRef` not attached | Make sure `ref={ref}` is on the wrapper div, not the button |
| Student navbar active link not highlighting | NavLink className not checking `isActive` | Use the function form: `className={({ isActive }) => isActive ? '...' : '...'}` |
| `line-clamp-2` not working | Tailwind v3 needs the plugin | Add `@tailwindcss/line-clamp` or use `overflow-hidden` with fixed height instead |
| Toast doesn't appear | `ToastProvider` not wrapping app | Check `main.tsx` — `<ToastProvider>` must wrap `<App />` |
| Violation payload shows `[object Object]` | Rendering object directly | Parse first: `const p = JSON.parse(violation.payload); p.blockedApp ?? p.activeWindow` |
| Performance chart points misaligned | Percentage math off | X position: `(index / (total - 1)) * 100`%, Y position: `(100 - scorePercent)`% |
