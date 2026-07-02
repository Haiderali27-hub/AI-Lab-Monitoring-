# SmartExam — Frontend Development Guide
### Stitch Export → React Components → Backend Connected

---

## Table of Contents

1. [Before You Start — Test Your Backend](#1-before-you-start--test-your-backend)
2. [React Project Setup](#2-react-project-setup)
3. [How to Export from Stitch into Antigravity](#3-how-to-export-from-stitch-into-antigravity)
4. [How to Convert Stitch HTML/CSS to React Components](#4-how-to-convert-stitch-htmlcss-to-react-components)
5. [Folder Structure](#5-folder-structure)
6. [The Connection Layer — Copy These Files Exactly](#6-the-connection-layer--copy-these-files-exactly)
7. [Building Each Page — Step by Step](#7-building-each-page--step-by-step)
8. [Antigravity Prompts — One Per Page](#8-antigravity-prompts--one-per-page)
9. [Build Order](#9-build-order)
10. [Common Errors and Fixes](#10-common-errors-and-fixes)

---

## 1. Before You Start — Test Your Backend

Do NOT touch the frontend until your backend responds correctly. Run these two checks first.

### Check 1 — Is the backend running?

```bash
cd SmartExam/Backend_API
dotnet run
```

You should see:
```
✅ Database seeded successfully.
Now listening on: http://localhost:5000
```

If you see an error about the database connection — open `appsettings.json` and make sure your Neon password is correct.

### Check 2 — Does login work?

Open a browser and go to:
```
http://localhost:5000/swagger
```

Find `POST /api/auth/login`, click **Try it out**, paste this body and hit Execute:

```json
{
  "email": "admin@smartexam.com",
  "password": "Admin@123"
}
```

If you get a `200 OK` response with a `token` field — your backend is ready. If not, fix the backend before continuing. The frontend cannot work without a working backend.

---

## 2. React Project Setup

Run these commands once. Do this before touching Stitch exports.

```bash
# Go to your SmartExam root folder (same level as Backend_API)
cd SmartExam

# Create the React project
npm create vite@latest Admin_Web_Panel -- --template react-ts
cd Admin_Web_Panel

# Install all dependencies at once
npm install axios @microsoft/signalr react-router-dom @tanstack/react-query lucide-react date-fns

# Install Tailwind
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p

# Start the dev server to confirm it works
npm run dev
# Should open at http://localhost:5173 showing the default Vite page
# Press Ctrl+C to stop
```

### Configure Tailwind

Replace the entire content of `tailwind.config.js`:

```js
/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        primary: {
          50:  '#EFF6FF',
          100: '#DBEAFE',
          500: '#2563EB',
          600: '#1D4ED8',
          700: '#1E40AF',
        },
      },
      fontFamily: {
        sans: ['Inter', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
```

Replace the entire content of `src/index.css`:

```css
@tailwind base;
@tailwind components;
@tailwind utilities;

@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap');

* {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
}

body {
  font-family: 'Inter', sans-serif;
  background-color: #F8FAFC;
  color: #0F172A;
}

/* Scrollbar styling */
::-webkit-scrollbar { width: 6px; }
::-webkit-scrollbar-track { background: #F1F5F9; }
::-webkit-scrollbar-thumb { background: #CBD5E1; border-radius: 3px; }
```

---

## 3. How to Export from Stitch into Antigravity

Follow these steps for EACH screen you designed in Stitch.

### Step 1 — Open your Stitch project
Go to your Stitch project. You should see all the screens you generated (Admin Portal, Teacher Portal, Student Desktop).

### Step 2 — Select a screen
Click on the screen you want to export first. Start with **Admin Login**.

### Step 3 — Export the HTML/CSS
Look for an **Export** button in Stitch (usually top right or in a menu). Select **HTML/CSS** as the export format. This will either:
- Download a ZIP file containing `index.html` and `style.css`
- Show you the code inline that you can copy

### Step 4 — Bring it into Antigravity
If it downloads a ZIP:
1. Unzip it
2. Put the files in a temporary folder inside your project: `Admin_Web_Panel/stitch_exports/login/`
3. Open Antigravity and point it at those files

If it shows code inline:
1. Copy the HTML
2. In Antigravity, create a new file `Admin_Web_Panel/stitch_exports/login.html`
3. Paste the HTML into it

### Step 5 — Repeat for every screen
Do this for all screens before you start converting. Having all exports ready first means you can work faster.

Suggested export folder names:
```
stitch_exports/
├── admin_login.html
├── admin_dashboard.html
├── admin_users.html
├── admin_device_bindings.html
├── admin_labs.html
├── admin_audit_logs.html
├── teacher_dashboard.html
├── teacher_create_exam.html
├── teacher_live_monitor.html
├── teacher_results.html
├── teacher_eligibility.html
├── student_login.html
├── student_dashboard.html
├── student_exam.html
└── student_violation.html
```

> These are temporary reference files. You will delete them after converting to React. They are just your visual source of truth.

---

## 4. How to Convert Stitch HTML/CSS to React Components

Stitch gives you raw HTML and CSS. You cannot use it directly in React. Here is the exact process to convert it.

### The conversion rule

| Stitch HTML/CSS | React equivalent |
|---|---|
| `class="..."` | `className="..."` |
| `style="color: red"` | `style={{ color: 'red' }}` |
| `<img src="...">` | `<img src="..." alt="..." />` (self-closing) |
| `onclick="..."` | `onClick={() => ...}` |
| Hardcoded text values | Replace with props or state variables |
| Hardcoded table rows | Replace with `.map()` over real data |
| Inline CSS colors | Replace with Tailwind classes |

### Example conversion

**Stitch gives you this HTML:**
```html
<div class="card">
  <h2 class="card-title">Total Students</h2>
  <p class="card-number">142</p>
</div>
```

**You convert it to this React component:**
```tsx
interface StatCardProps {
  title: string;
  value: number | string;
}

export function StatCard({ title, value }: StatCardProps) {
  return (
    <div className="bg-white border border-slate-200 rounded-lg p-6 shadow-sm">
      <h2 className="text-sm font-medium text-slate-500">{title}</h2>
      <p className="text-3xl font-bold text-slate-900 mt-1">{value}</p>
    </div>
  );
}
```

### The golden rule of conversion
**Never hardcode data in your React components.** Every piece of text that comes from the backend must be a variable. The Stitch design shows you the SHAPE of the UI — your React code makes it REAL with actual data.

### How to use Antigravity for conversion
For each Stitch export file, open it in Antigravity and use this prompt (see Section 8 for page-specific prompts):

```
I have this HTML/CSS from a design tool. Convert it into a clean React 
TypeScript component using Tailwind CSS classes instead of inline styles. 
Replace all hardcoded data with props. Keep the visual design exactly the same.
[paste your HTML here]
```

---

## 5. Folder Structure

Create this exact structure inside `Admin_Web_Panel/src/` before writing any component:

```
src/
│
├── api/                        ← All backend calls
│   ├── client.ts               ← Axios instance (auto-attaches JWT)
│   ├── auth.api.ts
│   ├── users.api.ts
│   ├── exams.api.ts
│   └── monitoring.api.ts
│
├── components/
│   ├── layout/
│   │   ├── AppLayout.tsx        ← Sidebar + Navbar wrapper
│   │   ├── AdminSidebar.tsx
│   │   ├── TeacherSidebar.tsx
│   │   └── Navbar.tsx
│   └── ui/
│       ├── Badge.tsx
│       ├── Button.tsx
│       ├── Card.tsx
│       ├── Input.tsx
│       ├── Modal.tsx
│       ├── Spinner.tsx
│       └── Toast.tsx
│
├── context/
│   └── AuthContext.tsx          ← Stores logged-in user + token
│
├── pages/
│   ├── auth/
│   │   └── LoginPage.tsx
│   ├── admin/
│   │   ├── DashboardPage.tsx
│   │   ├── UsersPage.tsx
│   │   ├── DeviceBindingsPage.tsx
│   │   ├── LabsPage.tsx
│   │   └── AuditLogsPage.tsx
│   └── teacher/
│       ├── TeacherDashboardPage.tsx
│       ├── CreateExamPage.tsx
│       ├── LiveMonitorPage.tsx
│       ├── ResultsPage.tsx
│       └── EligibilityPage.tsx
│
├── types/
│   └── index.ts                 ← TypeScript types matching backend
│
├── utils/
│   ├── constants.ts             ← API URL, role names
│   └── formatters.ts            ← Date/time helpers
│
├── App.tsx                      ← Router + route protection
├── main.tsx
└── index.css
```

Create all the folders now (they can be empty):

```bash
cd Admin_Web_Panel/src
mkdir -p api components/layout components/ui context pages/auth pages/admin pages/teacher types utils stitch_exports
```

---

## 6. The Connection Layer — Copy These Files Exactly

These files wire your React app to your backend. Copy them exactly — do not change anything except the base URL if your backend runs on a different port.

---

### `src/utils/constants.ts`

```typescript
export const API_BASE_URL = 'http://localhost:5000/api';
export const SIGNALR_HUB_URL = 'http://localhost:5000/hubs/monitoring';
```

---

### `src/types/index.ts`

```typescript
// ── Auth ──────────────────────────────────────────────────────
export type UserRole = 'SuperAdmin' | 'Admin' | 'Teacher' | 'Student';

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  userId: string;
  name: string;
  email: string;
  role: UserRole;
  deviceBound: boolean;
}

export interface AuthUser {
  userId: string;
  name: string;
  email: string;
  role: UserRole;
}

// ── Users ──────────────────────────────────────────────────────
export interface User {
  userId: string;
  name: string;
  email: string;
  role: UserRole;
  isActive: boolean;
  createdAt: string;
  deviceBound: boolean;
  deviceRegisteredAt: string | null;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: UserRole;
}

// ── Exams ──────────────────────────────────────────────────────
export type ExamStatus = 'Scheduled' | 'Active' | 'Ended';
export type QuestionType = 'Coding' | 'Theory';
export type SessionStatus = 'InProgress' | 'Submitted' | 'ForceSubmitted' | 'TimedOut';

export interface Exam {
  examId: string;
  title: string;
  courseName: string;
  sectionName: string;
  startTime: string;
  durationMinutes: number;
  status: ExamStatus;
  questionCount: number;
}

export interface Question {
  questionId: string;
  type: QuestionType;
  bodyText: string;
  marks: number;
  orderIndex: number;
  testCases: TestCase[];
}

export interface TestCase {
  testCaseId: string;
  input: string;
  expectedOutput: string;
  isHidden: boolean;
}

export interface ExamAssignment {
  assignmentId: string;
  examId: string;
  userId: string;
  studentName: string;
  studentEmail: string;
  workstationNumber: string | null;
  isEligible: boolean;
  eligibilityNote: string | null;
}

// ── Live Monitor ───────────────────────────────────────────────
export interface StudentLiveStatus {
  userId: string;
  studentName: string;
  workstationNumber: string;
  sessionId: string;
  lastHeartbeat: string;
  activeWindow: string;
  answeredCount: number;
  totalQuestions: number;
  violationCount: number;
  tileStatus: 'Normal' | 'Warning' | 'Violation';
}

// ── Results ────────────────────────────────────────────────────
export interface AnswerResult {
  answerId: string;
  questionId: string;
  questionText: string;
  questionType: QuestionType;
  marks: number;
  answerText: string;
  aiGrading: {
    suggestedMarks: number;
    justification: string;
    confidence: 'High' | 'Medium' | 'Low';
  } | null;
  teacherOverride: {
    finalMarks: number;
    note: string;
  } | null;
}

export interface PlagiarismFlag {
  plagId: string;
  questionId: string;
  questionText: string;
  studentAName: string;
  studentBName: string;
  similarityScore: number;
}
```

---

### `src/api/client.ts`

```typescript
import axios from 'axios';
import { API_BASE_URL } from '../utils/constants';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
});

// Automatically attach JWT to every request
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('smartexam_token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// If token expires, send user back to login
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('smartexam_token');
      localStorage.removeItem('smartexam_user');
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

export default apiClient;
```

---

### `src/api/auth.api.ts`

```typescript
import apiClient from './client';
import type { LoginRequest, LoginResponse, AuthUser } from '../types';

export const authApi = {
  login: (data: LoginRequest) =>
    apiClient.post<LoginResponse>('/auth/login', data).then(r => r.data),

  logout: () =>
    apiClient.post('/auth/logout'),

  me: () =>
    apiClient.get<AuthUser>('/auth/me').then(r => r.data),
};
```

---

### `src/api/users.api.ts`

```typescript
import apiClient from './client';
import type { User, CreateUserRequest, UserRole } from '../types';

export const usersApi = {
  getAll: (role?: UserRole) =>
    apiClient.get<User[]>('/users', { params: role ? { role } : undefined })
      .then(r => r.data),

  create: (data: CreateUserRequest) =>
    apiClient.post<User>('/users', data).then(r => r.data),

  deactivate: (id: string) =>
    apiClient.patch(`/users/${id}/deactivate`),

  resetDeviceBinding: (id: string) =>
    apiClient.delete(`/users/${id}/device-binding`),

  forceLogout: (id: string) =>
    apiClient.post(`/users/${id}/force-logout`),
};
```

---

### `src/api/exams.api.ts`

```typescript
import apiClient from './client';
import type { Exam, ExamAssignment } from '../types';

export const examsApi = {
  getAll: () =>
    apiClient.get<Exam[]>('/exams').then(r => r.data),

  getById: (id: string) =>
    apiClient.get(`/exams/${id}`).then(r => r.data),

  getAssignments: (examId: string) =>
    apiClient.get<ExamAssignment[]>(`/exams/${examId}/assignments`).then(r => r.data),

  updateEligibility: (examId: string, assignments: { userId: string; isEligible: boolean; eligibilityNote?: string }[]) =>
    apiClient.put(`/exams/${examId}/eligibility`, { assignments }),

  getResults: (examId: string) =>
    apiClient.get(`/exams/${examId}/results`).then(r => r.data),

  getPlagiarism: (examId: string) =>
    apiClient.get(`/exams/${examId}/plagiarism`).then(r => r.data),

  overrideGrade: (answerId: string, finalMarks: number, note: string) =>
    apiClient.post(`/answers/${answerId}/override`, { finalMarks, note }),

  forceSubmitAll: (examId: string) =>
    apiClient.post(`/exams/${examId}/force-submit-all`),
};
```

---

### `src/context/AuthContext.tsx`

```tsx
import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import type { AuthUser } from '../types';

interface AuthContextType {
  user: AuthUser | null;
  token: string | null;
  login: (token: string, user: AuthUser) => void;
  logout: () => void;
  isLoading: boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const t = localStorage.getItem('smartexam_token');
    const u = localStorage.getItem('smartexam_user');
    if (t && u) {
      setToken(t);
      setUser(JSON.parse(u));
    }
    setIsLoading(false);
  }, []);

  const login = (token: string, user: AuthUser) => {
    localStorage.setItem('smartexam_token', token);
    localStorage.setItem('smartexam_user', JSON.stringify(user));
    setToken(token);
    setUser(user);
  };

  const logout = () => {
    localStorage.removeItem('smartexam_token');
    localStorage.removeItem('smartexam_user');
    setToken(null);
    setUser(null);
    window.location.href = '/login';
  };

  return (
    <AuthContext.Provider value={{ user, token, login, logout, isLoading }}>
      {children}
    </AuthContext.Provider>
  );
}

// Use this hook in every page component that needs user info
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthProvider');
  return ctx;
}
```

---

### `src/App.tsx`

```tsx
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
  if (isLoading) return (
    <div className="min-h-screen flex items-center justify-center">
      <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
    </div>
  );
  if (!user) return <Navigate to="/login" replace />;
  if (!roles.includes(user.role)) return <Navigate to="/login" replace />;
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
```

---

### `src/components/layout/AppLayout.tsx`

```tsx
import { ReactNode } from 'react';
import { useAuth } from '../../context/AuthContext';
import AdminSidebar from './AdminSidebar';
import TeacherSidebar from './TeacherSidebar';
import Navbar from './Navbar';

interface AppLayoutProps {
  children: ReactNode;
  title: string;
}

export default function AppLayout({ children, title }: AppLayoutProps) {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin' || user?.role === 'SuperAdmin';

  return (
    <div className="flex h-screen bg-slate-50 overflow-hidden">
      {isAdmin ? <AdminSidebar /> : <TeacherSidebar />}
      <div className="flex-1 flex flex-col overflow-hidden">
        <Navbar title={title} />
        <main className="flex-1 overflow-y-auto p-8">
          {children}
        </main>
      </div>
    </div>
  );
}
```

---

## 7. Building Each Page — Step by Step

For every page follow this exact 4-step process:

```
Step 1 — Open the Stitch export HTML for that page in Antigravity
Step 2 — Use the Antigravity prompt from Section 8 to convert it to React
Step 3 — Wire the API call using the code shown below for each page
Step 4 — Test it: run the dev server, log in, navigate to the page, confirm real data loads
```

---

### PAGE 1 — Login Page
**File:** `src/pages/auth/LoginPage.tsx`
**Backend endpoint:** `POST /api/auth/login`
**Test:** After building, run the app, type `admin@smartexam.com` / `Admin@123`, hit Sign In. Should land on `/admin/dashboard`.

**API wiring — paste this logic into your LoginPage after Antigravity converts the HTML:**

```typescript
// Inside LoginPage component
const { login } = useAuth();
const [email, setEmail] = useState('');
const [password, setPassword] = useState('');
const [error, setError] = useState('');
const [loading, setLoading] = useState(false);
const navigate = useNavigate();

const handleSubmit = async () => {
  setError('');
  setLoading(true);
  try {
    const data = await authApi.login({ email, password });
    login(data.token, {
      userId: data.userId,
      name: data.name,
      email: data.email,
      role: data.role,
    });
    // Redirect based on role
    if (data.role === 'Teacher') navigate('/teacher/dashboard');
    else navigate('/admin/dashboard');
  } catch (err: any) {
    setError(err.response?.data?.message || 'Invalid email or password');
  } finally {
    setLoading(false);
  }
};

// Wire to your button: onClick={handleSubmit}
// Wire to email input: value={email} onChange={(e) => setEmail(e.target.value)}
// Wire to password input: value={password} onChange={(e) => setPassword(e.target.value)}
// Show error: {error && <p className="text-red-500 text-sm">{error}</p>}
// Show loading: disabled={loading} on button
```

---

### PAGE 2 — Admin Dashboard
**File:** `src/pages/admin/DashboardPage.tsx`
**Backend endpoints:** `GET /api/users`, `GET /api/exams`
**Test:** Stats cards show real counts from Neon database.

```typescript
// Inside DashboardPage — fetch real data on mount
const [users, setUsers] = useState<User[]>([]);
const [exams, setExams] = useState<Exam[]>([]);
const [loading, setLoading] = useState(true);

useEffect(() => {
  Promise.all([usersApi.getAll(), examsApi.getAll()])
    .then(([usersData, examsData]) => {
      setUsers(usersData);
      setExams(examsData);
    })
    .finally(() => setLoading(false));
}, []);

// Compute stats from real data
const studentCount = users.filter(u => u.role === 'Student').length;
const activeExams = exams.filter(e => e.status === 'Active').length;
const pendingBindingResets = users.filter(u => u.role === 'Student' && !u.deviceBound).length;

// Replace hardcoded numbers in your Stitch-converted JSX:
// "142" → {studentCount}
// "3"   → {activeExams}
// "2"   → {pendingBindingResets}
// Recent exams table → {exams.slice(0, 5).map(exam => <tr>...</tr>)}
```

---

### PAGE 3 — User Management
**File:** `src/pages/admin/UsersPage.tsx`
**Backend endpoints:** `GET /api/users`, `POST /api/users`, `PATCH /api/users/{id}/deactivate`, `DELETE /api/users/{id}/device-binding`, `POST /api/users/{id}/force-logout`
**Test:** Table loads real users. Add User form creates a real user visible in Neon.

```typescript
const [users, setUsers] = useState<User[]>([]);
const [activeTab, setActiveTab] = useState<UserRole | 'All'>('All');
const [search, setSearch] = useState('');
const [showAddModal, setShowAddModal] = useState(false);

// Load users on mount and after any change
const loadUsers = async () => {
  const data = await usersApi.getAll();
  setUsers(data);
};
useEffect(() => { loadUsers(); }, []);

// Client-side filter (no extra API calls needed)
const filtered = users.filter(u => {
  const matchesTab = activeTab === 'All' || u.role === activeTab;
  const matchesSearch = u.name.toLowerCase().includes(search.toLowerCase())
    || u.email.toLowerCase().includes(search.toLowerCase());
  return matchesTab && matchesSearch;
});

// Create user
const handleCreate = async (formData: CreateUserRequest) => {
  await usersApi.create(formData);
  setShowAddModal(false);
  loadUsers(); // refresh table
};

// Three-dot menu actions
const handleResetBinding = async (userId: string) => {
  if (confirm('Reset this student\'s device binding?')) {
    await usersApi.resetDeviceBinding(userId);
    loadUsers();
  }
};

const handleForceLogout = async (userId: string) => {
  await usersApi.forceLogout(userId);
  alert('Sessions revoked.');
};

// Wire filtered array to table:
// {filtered.map(user => <tr key={user.userId}>...</tr>)}
```

---

### PAGE 4 — Device Bindings
**File:** `src/pages/admin/DeviceBindingsPage.tsx`
**Backend endpoints:** `GET /api/users?role=Student`, `DELETE /api/users/{id}/device-binding`, `POST /api/users/{id}/force-logout`
**Test:** Only students shown. Reset binding clears the device record in Neon.

```typescript
const [students, setStudents] = useState<User[]>([]);
const [showUnboundOnly, setShowUnboundOnly] = useState(false);
const [confirmReset, setConfirmReset] = useState<User | null>(null);

useEffect(() => {
  usersApi.getAll('Student').then(setStudents);
}, []);

const displayed = showUnboundOnly
  ? students.filter(s => !s.deviceBound)
  : students;

const handleReset = async () => {
  if (!confirmReset) return;
  await usersApi.resetDeviceBinding(confirmReset.userId);
  setConfirmReset(null);
  // Refresh
  usersApi.getAll('Student').then(setStudents);
};

// Wire confirmReset to the modal:
// Show modal when confirmReset !== null
// "Reset Binding" button onClick → setConfirmReset(user)
// Modal "Confirm" button → handleReset()
// Modal "Cancel" button → setConfirmReset(null)
```

---

### PAGE 5 — Teacher Dashboard
**File:** `src/pages/teacher/TeacherDashboardPage.tsx`
**Backend endpoint:** `GET /api/exams`
**Test:** Exam list shows the seeded "Mid-Term Lab Exam".

```typescript
const { user } = useAuth();
const [exams, setExams] = useState<Exam[]>([]);

useEffect(() => {
  examsApi.getAll().then(setExams);
}, []);

const upcoming = exams.filter(e => e.status === 'Scheduled');
const active = exams.filter(e => e.status === 'Active');
const needsReview = exams.filter(e => e.status === 'Ended');

// Greeting uses real teacher name:
// "Good morning, {user?.name}"
```

---

### PAGE 6 — Create Exam
**File:** `src/pages/teacher/CreateExamPage.tsx`
**Backend endpoint:** `POST /api/exams` *(you will need to add this endpoint — see note below)*
**Test:** Fill all 4 steps and submit. New exam appears in teacher dashboard.

```typescript
// Multi-step form state
const [step, setStep] = useState(1);
const [formData, setFormData] = useState({
  // Step 1
  title: '',
  sectionId: '',
  startTime: '',
  durationMinutes: 60,
  instructions: '',
  // Step 2
  questions: [] as { type: QuestionType; bodyText: string; marks: number; testCases: { input: string; expectedOutput: string }[] }[],
  // Step 3
  allowedApps: '',
  aiEvaluationEnabled: true,
  plagiarismThreshold: 70,
  // Step 4
  assignments: [] as { userId: string; isEligible: boolean }[],
});

const handleFinalSubmit = async () => {
  await apiClient.post('/exams', formData);
  navigate('/teacher/dashboard');
};

// Step navigation:
// "Next" button → setStep(s => s + 1)
// "Back" button → setStep(s => s - 1)
// Step 4 "Create Exam" button → handleFinalSubmit()
```

> **Backend note:** You need to add `POST /api/exams` to your ExamsController that accepts the full exam payload including questions and assignments. Add this endpoint before building this page.

---

### PAGE 7 — Eligibility Management
**File:** `src/pages/teacher/EligibilityPage.tsx`
**Backend endpoints:** `GET /api/exams`, `GET /api/exams/{id}/assignments`, `PUT /api/exams/{id}/eligibility`
**Test:** Toggle a student off, save — refresh page and toggle should still be off.

```typescript
const [exams, setExams] = useState<Exam[]>([]);
const [selectedExamId, setSelectedExamId] = useState('');
const [assignments, setAssignments] = useState<ExamAssignment[]>([]);
const [hasChanges, setHasChanges] = useState(false);

useEffect(() => { examsApi.getAll().then(setExams); }, []);

useEffect(() => {
  if (selectedExamId) {
    examsApi.getAssignments(selectedExamId).then(setAssignments);
  }
}, [selectedExamId]);

const toggleEligibility = (userId: string) => {
  setAssignments(prev => prev.map(a =>
    a.userId === userId ? { ...a, isEligible: !a.isEligible } : a
  ));
  setHasChanges(true);
};

const handleSave = async () => {
  await examsApi.updateEligibility(selectedExamId,
    assignments.map(a => ({ userId: a.userId, isEligible: a.isEligible, eligibilityNote: a.eligibilityNote ?? '' }))
  );
  setHasChanges(false);
  alert('Eligibility saved.');
};

// Wire save button: onClick={handleSave} disabled={!hasChanges}
// Show "Unsaved changes" indicator when hasChanges === true
```

---

### PAGE 8 — Live Monitor
**File:** `src/pages/teacher/LiveMonitorPage.tsx`
**Backend:** `GET /api/exams?status=Active`, then SignalR hub
**Test:** Run the desktop app on the same machine as a student, start an exam — heartbeat tiles appear and update in real time.

```typescript
import * as signalR from '@microsoft/signalr';
import { SIGNALR_HUB_URL } from '../../utils/constants';

const { token } = useAuth();
const [activeExam, setActiveExam] = useState<Exam | null>(null);
const [studentStatuses, setStudentStatuses] = useState<Map<string, StudentLiveStatus>>(new Map());
const connectionRef = useRef<signalR.HubConnection | null>(null);

// Load active exam
useEffect(() => {
  examsApi.getAll().then(exams => {
    const active = exams.find(e => e.status === 'Active');
    setActiveExam(active ?? null);
  });
}, []);

// Connect SignalR when exam is active
useEffect(() => {
  if (!activeExam || !token) return;

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(SIGNALR_HUB_URL, { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();

  connection.on('StudentHeartbeat', (data) => {
    setStudentStatuses(prev => {
      const map = new Map(prev);
      const existing = map.get(data.userId) ?? {} as StudentLiveStatus;
      map.set(data.userId, { ...existing, ...data, lastHeartbeat: new Date().toISOString() });
      return map;
    });
  });

  connection.on('ViolationEvent', (data) => {
    setStudentStatuses(prev => {
      const map = new Map(prev);
      const existing = map.get(data.userId);
      if (existing) map.set(data.userId, { ...existing, violationCount: existing.violationCount + 1, tileStatus: 'Violation' });
      return map;
    });
  });

  connection.start().then(() => { connectionRef.current = connection; });
  return () => { connection.stop(); };
}, [activeExam, token]);

// Render student tiles from studentStatuses Map:
// {Array.from(studentStatuses.values()).map(s => <StudentTile key={s.userId} status={s} />)}

// Tile border color logic:
// tileStatus === 'Normal'    → border-l-4 border-green-500
// tileStatus === 'Warning'   → border-l-4 border-orange-400
// tileStatus === 'Violation' → border-l-4 border-red-500
```

---

### PAGE 9 — Results & Grading
**File:** `src/pages/teacher/ResultsPage.tsx`
**Backend endpoints:** `GET /api/exams/{id}/results`, `GET /api/exams/{id}/plagiarism`, `POST /api/answers/{id}/override`
**Test:** After an exam ends and AI runs, grades appear. Override one mark and confirm it saves.

```typescript
const { examId } = useParams();
const [results, setResults] = useState<any[]>([]);
const [plagiarism, setPlagiarism] = useState<PlagiarismFlag[]>([]);
const [activeTab, setActiveTab] = useState<'submissions' | 'plagiarism'>('submissions');
const [expandedStudent, setExpandedStudent] = useState<string | null>(null);

useEffect(() => {
  if (!examId) return;
  examsApi.getResults(examId).then(setResults);
  examsApi.getPlagiarism(examId).then(setPlagiarism);
}, [examId]);

const handleOverride = async (answerId: string, marks: number, note: string) => {
  await examsApi.overrideGrade(answerId, marks, note);
  // Refresh results
  examsApi.getResults(examId!).then(setResults);
};

// Expand a student row to show full review panel:
// onClick={() => setExpandedStudent(expandedStudent === userId ? null : userId)}
```

---

## 8. Antigravity Prompts — One Per Page

Use these prompts in Antigravity. For each page: first export the HTML from Stitch, then open Antigravity, then paste the prompt below followed by your HTML.

---

### Login Page Prompt
```
I'm building a React TypeScript app called SmartExam using Tailwind CSS.

Convert the attached Stitch HTML/CSS export into a React component file at 
src/pages/auth/LoginPage.tsx

Rules:
- Replace all class= with className=
- Replace all hardcoded values with React state variables
- The email input should use useState for its value
- The password input should use useState with a show/hide toggle
- The Sign In button should call a handleSubmit async function
- Show an error message below the button if login fails
- Show a loading spinner on the button while the request is pending
- Do not add any backend logic — just the UI with state. I will wire the API separately.
- Use Tailwind classes. Remove all inline styles.
- Export default the component.

[paste your admin_login.html content here]
```

---

### Admin Dashboard Prompt
```
Convert this Stitch HTML/CSS into src/pages/admin/DashboardPage.tsx

Rules:
- Import and use AppLayout from ../../components/layout/AppLayout with title="Dashboard"
- The 4 stat cards should accept props: title, value, icon, color
- The recent exams table should accept an array prop and render rows with .map()
- The recent activity list should accept an array prop and render with .map()
- All numbers (142, 3, 4, 2) should be replaced with prop variables
- Use Tailwind. No inline styles.

[paste your admin_dashboard.html content here]
```

---

### User Management Prompt
```
Convert this Stitch HTML/CSS into src/pages/admin/UsersPage.tsx

Rules:
- Import AppLayout with title="User Management"
- The tab bar (All / Students / Teachers / Admins) uses useState activeTab
- The search bar uses useState search
- The user table accepts a users array and renders rows with .map()
- Each row's three-dot menu should have onClick handlers: onResetBinding, onForceLogout, onDeactivate
- The Add User slide-over panel uses useState showPanel
- The Add User form has controlled inputs with useState for each field
- The form has a handleSubmit function that is empty for now (I will wire the API)
- Role badge colors: Student=blue, Teacher=purple, Admin=gray
- Status badge: Active=green, Inactive=gray
- Device Bound chip: Yes=blue, No=gray

[paste your admin_users.html content here]
```

---

### Device Bindings Prompt
```
Convert this Stitch HTML into src/pages/admin/DeviceBindingsPage.tsx

Rules:
- Import AppLayout with title="Device Bindings"
- Table accepts a students array prop, renders with .map()
- "Show unbound only" uses useState showUnboundOnly boolean toggle
- Each row has a "Reset Binding" button with onClick={() => onResetClick(student)}
- Confirmation modal uses useState confirmStudent — shows when confirmStudent is not null
- Modal "Confirm" and "Cancel" buttons call onConfirm and onCancel props
- Bound badge: green. Unbound badge: gray.

[paste your admin_device_bindings.html content here]
```

---

### Teacher Dashboard Prompt
```
Convert this Stitch HTML into src/pages/teacher/TeacherDashboardPage.tsx

Rules:
- Import AppLayout with title="Dashboard"
- Greeting text uses a name prop ("Good morning, {name}")
- The 3 stat cards accept props
- The upcoming exams list accepts an exams array and renders with .map()
- Each exam item has a "Manage" button with an onClick prop
- "Needs Your Review" list accepts an array and renders with .map()

[paste your teacher_dashboard.html content here]
```

---

### Create Exam Prompt
```
Convert this Stitch HTML into src/pages/teacher/CreateExamPage.tsx

Rules:
- Import AppLayout with title="Create New Exam"
- The step indicator uses useState step (1, 2, 3, or 4)
- Show only the current step's form based on step value
- Step 1 fields: title, sectionId (dropdown), startTime, durationMinutes, instructions — all useState
- Step 2: questions array in useState. "Add Question" adds a blank question object. Each question has type, bodyText, marks, testCases array.
- Step 3: allowedApps, aiEvaluationEnabled, plagiarismThreshold — all useState
- Step 4: shows a student list with checkboxes for assignment — useState assignments array
- "Next" button increments step, "Back" decrements
- Final "Create Exam" button calls an empty handleSubmit for now

[paste your teacher_create_exam.html content here]
```

---

### Live Monitor Prompt
```
Convert this Stitch HTML into src/pages/teacher/LiveMonitorPage.tsx

Rules:
- No sidebar for this page — it is full width with its own top bar
- The student grid accepts a studentStatuses array and renders tiles with .map()
- Each tile is its own StudentTile component accepting a status prop
- Tile border color: green if status=Normal, orange if status=Warning, red if status=Violation
- Clicking a tile opens a right panel — useState selectedStudent
- The right panel shows the selected student's activity and has a "Send Warning" input and "Force Submit" button
- All data is props for now — I will wire SignalR separately
- Timer display uses monospace font

[paste your teacher_live_monitor.html content here]
```

---

### Results Prompt
```
Convert this Stitch HTML into src/pages/teacher/ResultsPage.tsx

Rules:
- Import AppLayout with title="Results"
- Tab bar: "All Submissions" and "Plagiarism Flags" — useState activeTab
- Submissions table accepts results array, renders with .map()
- Clicking a row expands an inline review panel below it — useState expandedStudentId
- The review panel shows question text, student answer in a dark code block, and AI grading info
- Override section has number input and note input — useState overrideMark, overrideNote
- "Save Override" button calls an empty handleOverride(answerId, mark, note) for now
- Plagiarism tab shows flagged pairs as cards with similarity score badges
- "View Comparison" expands a side-by-side code diff panel below the card

[paste your teacher_results.html content here]
```

---

### Eligibility Prompt
```
Convert this Stitch HTML into src/pages/teacher/EligibilityPage.tsx

Rules:
- Import AppLayout with title="Eligibility Management"  
- Exam selector is a dropdown — useState selectedExamId
- Student table accepts assignments array, renders with .map()
- Each row's eligibility toggle uses onClick to call toggleEligibility(userId)
- The note field is an inline input — onChange updates the note in the assignments array
- "Mark All Eligible" / "Mark All Ineligible" buttons update all rows at once
- hasChanges boolean shows/hides the "Unsaved changes" indicator
- "Save Changes" button calls empty handleSave for now

[paste your teacher_eligibility.html content here]
```

---

## 9. Build Order

Follow this order exactly. Each step must work before moving to the next.

```
WEEK 1 — Make login work end-to-end

  □ Step 1: Copy all files from Section 6 into your project
  □ Step 2: Run npm run dev — confirm app loads at localhost:5173
  □ Step 3: Export admin_login.html from Stitch
  □ Step 4: Use Antigravity Login prompt to convert it
  □ Step 5: Wire the handleSubmit API call (Section 7, Page 1)
  □ Step 6: Test — admin@smartexam.com / Admin@123 → lands on /admin/dashboard
  □ Step 7: Test — teacher@smartexam.com / Teacher@123 → lands on /teacher/dashboard
  ✓ CHECKPOINT: Login works for both roles

WEEK 2 — Admin pages

  □ Build DashboardPage → real stats from API
  □ Build UsersPage → table loads, Add User works, reset binding works
  □ Build DeviceBindingsPage → reset with confirmation modal works
  ✓ CHECKPOINT: Admin can log in, see real data, manage users

WEEK 3 — Teacher core pages

  □ Build TeacherDashboardPage → exam list loads
  □ Build EligibilityPage → toggle and save works
  □ Build CreateExamPage → multi-step form submits to backend
  ✓ CHECKPOINT: Teacher can create an exam and manage eligibility

WEEK 4 — Live monitor + results

  □ Build LiveMonitorPage UI (no SignalR yet — static tiles)
  □ Wire SignalR to LiveMonitorPage (Section 7, Page 8)
  □ Build ResultsPage → AI grades display, override works
  ✓ CHECKPOINT: Full demo flow works end-to-end

STRETCH (if time)
  □ LabsPage
  □ AuditLogsPage
  □ CSV bulk upload on UsersPage
```

---

## 10. Common Errors and Fixes

| Error | Cause | Fix |
|---|---|---|
| `CORS error` in browser console | Backend not allowing your React port | Make sure `Program.cs` has `.WithOrigins("http://localhost:5173")` |
| `401 Unauthorized` on every request | Token not attached | Check `client.ts` — the interceptor must read from `localStorage.getItem('smartexam_token')` |
| `Cannot find module '../../context/AuthContext'` | Wrong import path | Check your folder structure matches Section 5 exactly |
| Login succeeds but page stays on `/login` | navigate() called before login() saves to context | Call `login(token, user)` before `navigate()` |
| White screen after login | Route in App.tsx not matching | Check the role — Admin routes need `roles={['Admin','SuperAdmin']}` |
| SignalR `connection failed` | Backend hub not registered | Make sure `app.MapHub<MonitoringHub>("/hubs/monitoring")` is in `Program.cs` |
| Tailwind classes not working | Content paths wrong | Check `tailwind.config.js` — content must include `./src/**/*.{ts,tsx}` |
| Stitch HTML has `onclick` not working in React | HTML attribute vs React prop | Change all `onclick` → `onClick`, all `class` → `className` |
| `Objects are not valid as a React child` | Rendering an object directly | Wrap with `JSON.stringify()` while debugging or access specific property |
| Data loads but table is empty | `.map()` on undefined | Add optional chaining: `data?.map(...)` or initialize state as `[]` |
