# SmartExam — Frontend UI Guide
### Stitch Prompts + Screen Specifications + Backend Connection Documentation

---

## Table of Contents

1. [Design System](#1-design-system)
2. [Portals Overview](#2-portals-overview)
3. [Stitch Prompts — Admin Portal](#3-stitch-prompts--admin-portal)
4. [Stitch Prompts — Teacher Portal](#4-stitch-prompts--teacher-portal)
5. [Stitch Prompts — Student Desktop App](#5-stitch-prompts--student-desktop-app)
6. [How to Use Stitch Exports](#6-how-to-use-stitch-exports)
7. [React Project Setup](#7-react-project-setup)
8. [Folder Structure](#8-folder-structure)
9. [Backend Connection Layer](#9-backend-connection-layer)
10. [Page-by-Page Backend Wiring](#10-page-by-page-backend-wiring)
11. [Auth Flow & Route Protection](#11-auth-flow--route-protection)
12. [Real-Time SignalR Connection](#12-real-time-signalr-connection)
13. [Build Order](#13-build-order)

---

## 1. Design System

Use these values consistently across every screen you generate in Stitch. Paste this block at the top of every Stitch prompt.

```
Design System:
- Font: Inter (headings bold, body regular)
- Primary color: #2563EB (blue)
- Primary dark: #1D4ED8
- Primary light: #EFF6FF
- Background: #F8FAFC
- Surface (cards): #FFFFFF
- Border: #E2E8F0
- Text primary: #0F172A
- Text secondary: #64748B
- Text muted: #94A3B8
- Success: #16A34A
- Warning: #D97706
- Danger: #DC2626
- Border radius: 8px for cards, 6px for inputs, 4px for badges
- Shadow: subtle drop shadow on cards (0 1px 3px rgba(0,0,0,0.1))
- Sidebar width: 240px, white background with left blue border on active item
- Top navbar height: 56px, white, bottom border #E2E8F0
- All inputs: white background, 1px border #E2E8F0, 40px height
- Primary button: #2563EB background, white text, 8px radius
- Spacing unit: 8px base (padding 16px, 24px, 32px)
```

---

## 2. Portals Overview

SmartExam has **two web portals** (Admin and Teacher share one React app with role-based views) and **one desktop app** (Student, built in WPF — Stitch gives visual reference only for the desktop app).

| Portal | Who Uses It | Tech | URL |
|--------|-------------|------|-----|
| **Admin Portal** | Admin users | React + TypeScript | `http://localhost:5173` |
| **Teacher Portal** | Teacher users | React + TypeScript | Same app, different routes |
| **Student Desktop App** | Students | WPF C# (Stitch = visual reference) | Desktop application |

The Admin and Teacher portals are the **same React project** — the sidebar and available routes change based on the logged-in user's role.

---

## 3. Stitch Prompts — Admin Portal

Generate each screen separately in Stitch. Name each screen exactly as shown.

---

### Screen A1 — Login Page
**Stitch Screen Name:** `SmartExam - Login`

```
Design a clean, minimal login page for a university exam monitoring system called SmartExam.

Design system: [paste the design system block from Section 1]

Layout:
- Full page centered vertically and horizontally
- Left half: solid #2563EB blue background with the SmartExam logo (a shield icon with a checkmark) centered in white, large bold white text "SmartExam" below it, and a subtitle "AI-Driven Lab Exam Monitoring System" in semi-transparent white
- Right half: white background with a centered login card (no border, just padding 48px)

Login card contents (top to bottom):
- Small text "Welcome back" in #64748B
- Bold heading "Sign in to your account" in #0F172A, 24px
- 24px gap
- Label "Email address" then input field with placeholder "admin@university.edu"
- 16px gap
- Label "Password" then input field with eye toggle icon on the right, placeholder "Enter your password"
- 24px gap
- Full width primary blue button "Sign In" (height 44px)
- 16px gap below button
- Small centered muted text "SmartExam v1.0 — University Lab Management"

No sign up link. No forgot password. This is an internal system.
Responsive: on mobile stack left panel above the form (condensed).
```

---

### Screen A2 — Admin Dashboard
**Stitch Screen Name:** `SmartExam - Admin Dashboard`

```
Design a clean admin dashboard page for SmartExam university exam monitoring system.

Design system: [paste design system block]

Layout: fixed left sidebar (240px) + top navbar (56px) + main content area.

Sidebar:
- White background, subtle right border
- Top: SmartExam logo (shield icon) + "SmartExam" text in bold blue + "Admin Panel" in small muted text below
- Navigation items with icons (active item has left blue border 3px and light blue background #EFF6FF):
  * Dashboard (grid icon) — ACTIVE
  * Users (people icon)
  * Labs & Workstations (building icon)
  * Exams (clipboard icon)
  * Device Bindings (laptop icon)
  * Audit Logs (list icon)
- Bottom: user avatar + "Admin User" name + "Admin" role badge + logout icon

Top navbar:
- White, bottom border
- Left: Page title "Dashboard" in bold #0F172A
- Right: notification bell icon + avatar

Main content:
- Page heading "Overview" with today's date subtitle in muted text
- Stats row — 4 equal cards in a grid:
  * "Total Students" — number 142 — people icon in blue circle
  * "Active Exams" — number 3 — clipboard icon in green circle
  * "Labs" — number 4 — building icon in blue circle
  * "Pending Device Resets" — number 2 — warning icon in orange circle
- 24px gap
- Two column layout (60/40 split):
  * Left: "Recent Exams" card — table with columns: Exam Title, Course, Status badge (Scheduled=blue, Active=green, Ended=gray), Date, Students. Show 5 rows of realistic data.
  * Right: "Recent Logins" card — list of 5 items each showing avatar initial circle + student name + "Logged in" + time ago in muted text
```

---

### Screen A3 — User Management
**Stitch Screen Name:** `SmartExam - User Management`

```
Design a user management page for SmartExam admin portal.

Design system: [paste design system block]

Same sidebar and navbar as Dashboard. Sidebar active item: Users.

Main content:
- Page heading "User Management"
- Tab bar below heading: "All Users" | "Students" | "Teachers" | "Admins" — tabs are underline style, active tab in blue
- Action bar below tabs: left side has search input (placeholder "Search by name or email...") with search icon, right side has two buttons: "Upload CSV" (outlined blue button with upload icon) and "Add User" (solid blue button with plus icon)
- User table below:
  Columns: checkbox | Name (with avatar initial circle) | Email | Role badge | Status badge (Active=green, Inactive=gray) | Device Bound (yes=blue chip, no=gray chip) | Created | Actions
  Show 6-8 realistic rows mixing students and teachers
  Actions column: three-dot menu icon
- Pagination at bottom: "Showing 1-8 of 142 users" + prev/next buttons

Add User modal (show it open on the side as a slide-over panel from the right, 480px wide):
- Header "Add New User" with close X
- Form fields: Full Name, Email Address, Password, Role (dropdown: Student / Teacher / Admin)
- Footer: Cancel button + "Create User" blue button
```

---

### Screen A4 — Device Bindings
**Stitch Screen Name:** `SmartExam - Device Bindings`

```
Design a device bindings management page for SmartExam admin portal.

Design system: [paste design system block]

Same sidebar/navbar layout. Active sidebar item: Device Bindings.

Main content:
- Page heading "Device Bindings" with subtitle "Manage student machine registrations"
- Info banner (light blue background #EFF6FF, blue left border 4px): "Device binding locks each student account to a specific lab machine. Reset a binding to allow the student to re-register on a new machine."
- Search bar + "Show unbound only" toggle switch on the right
- Table with columns:
  Student Name | Student Email | Machine Status (Bound = green badge, Unbound = gray badge) | Registered Date | Last Seen | Actions
  Show 8 rows. Some bound, 2 unbound.
  Actions: "Reset Binding" red outlined button + "Force Logout" gray button (only shown on bound rows)
- Confirmation modal shown (centered overlay with backdrop):
  - Warning icon in orange circle
  - Heading "Reset Device Binding?"
  - Body text "This will remove Ali Hassan's device registration. They will need to log in from a lab machine to re-register."
  - Two buttons: "Cancel" outlined + "Reset Binding" red solid
```

---

### Screen A5 — Labs & Workstations
**Stitch Screen Name:** `SmartExam - Labs & Workstations`

```
Design a labs and workstations management page for SmartExam admin portal.

Design system: [paste design system block]

Same sidebar/navbar. Active: Labs & Workstations.

Main content:
- Page heading "Labs & Workstations"
- "Add Lab" blue button top right
- Lab cards in a grid (2 columns):
  Each card shows:
  - Lab name bold (e.g. "Lab A — Block 3") 
  - Location in muted text
  - Workstation count chip ("12 Workstations" in blue)
  - Expandable workstation table inside the card (show it expanded on one card):
    Columns: Machine No. | IP Address | Status (Available/In Use) | Actions (Edit/Delete icon)
    Show 6 workstation rows
  - "Add Workstation" text button at bottom of expanded card
  - Card has a subtle border and collapse/expand chevron icon top right
```

---

### Screen A6 — Audit Logs
**Stitch Screen Name:** `SmartExam - Audit Logs`

```
Design an audit log viewer page for SmartExam admin portal.

Design system: [paste design system block]

Same sidebar/navbar. Active: Audit Logs.

Main content:
- Page heading "Audit Logs" with export button (outlined) top right
- Filter bar: Date range picker | Event Type dropdown (Login / Exam Start / Submission / Violation / Grade Override / Device Reset) | User search input | "Apply Filters" blue button
- Log table:
  Columns: Timestamp | Actor (name + role badge) | Event Type (colored badge: Login=blue, Violation=red, Submission=green, Override=orange) | Entity | Details (truncated with "View" link)
  Show 10 rows of realistic mixed events
- Click "View" opens a detail drawer from the right (360px) showing full JSON payload in a monospace code block with syntax highlighting
```

---

## 4. Stitch Prompts — Teacher Portal

These screens appear when a Teacher role logs in. The sidebar changes.

---

### Screen T1 — Teacher Dashboard
**Stitch Screen Name:** `SmartExam - Teacher Dashboard`

```
Design a teacher dashboard for SmartExam exam monitoring system.

Design system: [paste design system block]

Same layout: sidebar (240px) + navbar (56px) + content.

Teacher sidebar navigation (active: Dashboard):
  * Dashboard (grid icon)
  * My Exams (clipboard icon)
  * Create Exam (plus-circle icon)
  * Live Monitor (radio icon — with a green pulsing dot when exam is active)
  * Results & Grading (chart icon)
  * Eligibility (check-circle icon)

Main content:
- Heading "Good morning, Dr. Ahmed" subtitle "Tuesday, 21 May 2025"
- Stats row (3 cards):
  * "My Exams" — 8 total
  * "Active Now" — 1 — green with pulsing dot
  * "Pending Reviews" — 3 — orange
- "Upcoming Exams" card below — timeline list style:
  Each item: colored left border | Exam title bold | Course name muted | Date + time | Duration | Student count | Status badge | "View" button
  Show 3 upcoming exams
- "Exams Needing Review" card — list of 2 exams with orange "Review Grades" button each
```

---

### Screen T2 — Create Exam
**Stitch Screen Name:** `SmartExam - Create Exam`

```
Design a multi-step exam creation form for SmartExam teacher portal.

Design system: [paste design system block]

Sidebar + navbar layout. Active: Create Exam.

Main content:
- Page heading "Create New Exam"
- Step indicator at the top (horizontal stepper, 4 steps):
  Step 1 "Basic Info" — ACTIVE (blue filled circle with number, blue connecting line to next)
  Step 2 "Questions" — upcoming (gray circle)
  Step 3 "Settings" — upcoming
  Step 4 "Students" — upcoming

Step 1 form (shown active):
- Two column grid layout:
  Left column: Exam Title (full width input), Course/Section (dropdown), Instructions (textarea 4 rows)
  Right column: Exam Date (date picker), Start Time (time picker), Duration in Minutes (number input with + - buttons), 
- "Next: Add Questions" blue button bottom right, "Cancel" text button bottom left

Below that, show Step 2 in a second design variant (same page, label it "Step 2 variant"):
- "Add Question" button top right
- Question cards listed (2 shown):
  Card 1: "Question 1" badge + "Coding" type badge blue | Question text | Marks input | "Add Test Cases" collapsible section showing 2 test case rows (Input + Expected Output inputs)  | drag handle icon left | edit/delete icons right
  Card 2: "Question 2" + "Theory" badge gray | Question text | Marks input
- "Back" outlined + "Next: Settings" blue buttons
```

---

### Screen T3 — Live Proctoring Dashboard
**Stitch Screen Name:** `SmartExam - Live Proctoring`

```
Design a live exam proctoring dashboard for a teacher in SmartExam.

Design system: [paste design system block]

Full-width layout. Sidebar collapsed to icon-only (48px) to maximise screen space.

Top bar:
- Left: "Live Monitor" heading + exam name "Mid-Term Lab Exam — CS301" in muted text
- Center: Live timer showing exam time remaining "00:47:23" in large monospace font, red when under 5 min
- Right: "Force Submit All" red outlined button + student count "24 / 28 Active" green chip

Student grid (main content area):
- 4-column responsive grid of student tiles
- Each tile is a card (white, border, 8px radius, padding 16px):
  - Top row: student name bold + workstation number chip (e.g. "PC-04") right aligned
  - Second row: last heartbeat "● 3s ago" in green (dot pulses), or "● 28s ago" in orange, or "● 2m ago" in red
  - Third row: active window title in small muted monospace text (e.g. "code.exe — main.cpp")
  - Bottom row: progress "2 / 3 answered" gray text + violation count if any ("2 violations" red badge)
  - Card border color: green (normal) | yellow (warning — suspicious activity) | red (violation)
- Show 12 tiles. 8 green, 2 yellow (one showing "Window: chrome.exe" warning), 2 red (showing violation count)

Right panel (shown sliding in, 320px):
- Triggered by clicking a student tile
- Header: student name + close X
- "Activity Timeline" heading
- Vertical timeline list of events (newest first):
  Each event: timestamp | icon | event description
  Colors: Heartbeat=gray, WindowFocus=blue, Violation=red
- Bottom: "Send Warning" input + send button, "Force Submit" red button
```

---

### Screen T4 — Results & AI Grading
**Stitch Screen Name:** `SmartExam - Results & Grading`

```
Design an exam results and AI grading review page for SmartExam teacher portal.

Design system: [paste design system block]

Sidebar + navbar. Active: Results & Grading.

Main content:
- Page heading "Results — Mid-Term Lab Exam" + "CS301 • BSCS-6A • 28 Students" subtitle
- Summary stats bar (4 inline stats with dividers): Average Score 67% | Highest 94% | Lowest 32% | Plagiarism Flags 3
- Tab bar: "All Submissions" | "Plagiarism Flags" (with red badge count 3) | "Grade Summary"

"All Submissions" tab (shown active):
- Table: Student Name | Q1 (AI mark / total) | Q2 (AI mark / total) | Total | Status badge (Reviewed / Pending Review) | Actions "Review" button
- Show 8 rows. Some pending, some reviewed. AI marks shown as "18/20" with a small robot icon.

Click "Review" opens full-width review panel below (show it expanded for one student):
- Student name heading + "Ali Hassan — PC-01"
- Two question panels side by side:
  Left: Question text + "Student's Answer" code block (dark background, monospace)
  Right: AI Evaluation box (light blue background):
    - "AI Suggested Mark: 16 / 20" bold
    - Confidence badge "High" green
    - Justification text in muted italic
    - Override section: "Override Mark" number input + "Note" input + "Save Override" blue button

"Plagiarism Flags" tab (show as second variant):
- 3 flagged pairs listed as cards:
  Each card: two student names side by side with "vs" in middle + similarity percentage badge (e.g. "87% Similar" red) + Question label + "View Comparison" button
- Click opens side-by-side diff view (two code blocks, matching lines highlighted in yellow)
```

---

### Screen T5 — Eligibility Management
**Stitch Screen Name:** `SmartExam - Eligibility`

```
Design an exam eligibility management page for SmartExam teacher portal.

Design system: [paste design system block]

Sidebar + navbar. Active: Eligibility.

Main content:
- Page heading "Eligibility — Mid-Term Lab Exam"
- Exam selector dropdown at top
- Action bar: "Mark All Eligible" green button | "Mark All Ineligible" red outlined button | student count chip
- Student table:
  Columns: Student Name | Student Email | Workstation | Eligibility (toggle switch — green=eligible, gray=ineligible) | Note (editable inline text "Add reason..." placeholder) | Last Updated
  Show 8 students. 6 eligible toggles on, 2 off with reason notes like "Attendance shortage"
- Save button fixed at bottom right: "Save Changes" blue button with unsaved changes indicator dot
```

---

## 5. Stitch Prompts — Student Desktop App

These are WPF screens — Stitch gives you the visual design reference. Your developer implements these in C# WPF.

---

### Screen S1 — Student Login Window
**Stitch Screen Name:** `SmartExam - Student Login (Desktop)`

```
Design a desktop application login window for SmartExam student exam software. This is a WPF Windows desktop app, not a website.

Design system: [paste design system block]

Window size: 440px wide × 560px tall. Centered on screen. No title bar chrome (custom title bar).

Custom title bar at top: dark blue #1D4ED8 background, height 36px, "SmartExam" white text left with shield icon, minimize and close buttons right.

Main area white background:
- Top section (blue #2563EB, 140px tall): 
  Large white shield icon (64px) centered
  "SmartExam" white bold text below icon
  "Secure Exam Environment" white small text

- Form section (white, padding 32px):
  "Student Login" bold heading #0F172A
  "Please use your university credentials" muted subtitle
  24px gap
  "Student Email" label + email input with person icon left
  16px gap
  "Password" label + password input with lock icon left + eye toggle right
  24px gap
  "Sign In" full-width blue button (44px height)
  16px gap
  Small centered muted text: "Your device will be registered on first login"
  
- Bottom strip: light gray #F8FAFC, 36px, centered muted text "v1.0.0 • Connected to SmartExam Server"
```

---

### Screen S2 — Exam Dashboard (Desktop)
**Stitch Screen Name:** `SmartExam - Student Exam Dashboard (Desktop)`

```
Design a student exam dashboard for the SmartExam desktop application.

Design system: [paste design system block]

Desktop window, 900px × 640px, custom dark blue title bar with "SmartExam" and student name right-aligned + logout icon.

Left panel (280px, white, right border):
- Student info card at top: avatar circle (initials) + student name bold + email muted + "Device Registered ✓" green chip
- "My Exams" heading below
- Vertical exam list: each item is a selectable row with:
  Exam title | course code small | status badge
  Active/selected item has blue left border and light blue background

Right panel (remaining width, #F8FAFC background):
- When an exam is selected, show exam detail card (white, padding 32px, centered):
  Exam title large bold
  Course name + section muted
  Status badge large
  Divider
  Two-column info grid: Date | Time | Duration | Room | Workstation
  Divider  
  "Instructions" heading + instruction text paragraph muted
  Divider
  Countdown timer section:
    "Exam starts in" muted label
    Large monospace countdown "02 : 14 : 33" (hours:min:sec) in blue
    (If exam is active: countdown shows time remaining in red)
  Full-width "Start Exam" button (44px, blue) — grayed out with tooltip "Available at exam time" if not yet time, active blue when exam time is reached
```

---

### Screen S3 — Secure Exam Environment (Desktop)
**Stitch Screen Name:** `SmartExam - Exam Environment (Desktop)`

```
Design the secure exam-taking window for SmartExam student desktop application.

Design system: [paste design system block]

Full screen application window (no OS taskbar visible, this IS the entire screen). Dark theme for this window only.

Background: #0F172A dark navy.

Top bar (56px, slightly lighter #1E293B, border bottom #334155):
- Left: "SmartExam Secure Mode" white text + red pulsing dot + "EXAM ACTIVE" red badge
- Center: Exam title white bold
- Right: Timer "Time Remaining: 00:47:12" white monospace (turns red under 10 min) + "Save" white outlined button

Left panel (260px, #1E293B, question navigator):
- "Questions" heading white small
- Vertical list of question number buttons (Q1, Q2, Q3):
  Square buttons, 44px, border
  Answered = blue filled
  Current = white border blue text
  Unanswered = dark gray
- Below list: progress bar "2 of 3 answered" with blue fill

Main content area (#0F172A):
For a coding question (shown active):
- Question text panel (top 30%): white text on #1E293B panel, question body, marks badge top right "20 Marks"
- Code editor (bottom 70%): dark code editor (VSCode style — #1E1E1E background), language selector dropdown top right ("C++" selected), line numbers, syntax highlighting, blinking cursor

For a theory question (shown in small preview bottom left):
- White text area on dark background

Bottom bar (48px, #1E293B):
- Left: "Auto-saved 30s ago" muted green text with checkmark
- Right: "Previous" outlined button + "Next" outlined button + "Submit Exam" red button (confirm on click)
```

---

### Screen S4 — Violation Warning Overlay (Desktop)
**Stitch Screen Name:** `SmartExam - Violation Warning (Desktop)`

```
Design a violation warning overlay for SmartExam student desktop exam application.

Design system: [paste design system block]

Show the Exam Environment screen (dark, from S3) in the background, dimmed with a dark overlay.

Centered modal (white, 420px wide, 280px tall, 12px radius):
- Top section: orange/red warning icon (48px, triangle with exclamation) centered, orange color #D97706
- Heading "Unauthorized Activity Detected" bold #0F172A, centered, 18px
- Body text centered muted: "An attempt to open an unauthorized application was blocked. This violation has been recorded."
- Violation detail chip: "Blocked: chrome.exe" red chip centered
- Warning count: "⚠ Warning 2 of 3 — A third violation will notify your proctor immediately." orange small text centered
- Single button: "I Understand" full-width blue button

Outside the modal, top right corner: "Proctor has been notified" small white text on dark overlay, notification bell icon
```

---

## 6. How to Use Stitch Exports

Once you generate each screen in Stitch, follow these steps to get usable code:

### Step 1 — Generate All Screens
Generate every screen listed above (A1–A6, T1–T5, S1–S4). Name them exactly as specified. Keep them in one Stitch project called **"SmartExam UI"**.

### Step 2 — Export from Stitch
For each screen in Stitch:
1. Click the screen
2. Look for **Export** or **Get Code** option
3. Export as **React + Tailwind** if available, otherwise export as **HTML/CSS**
4. Download the export ZIP for each screen

### Step 3 — What to Take From the Export
You do NOT copy the exported code directly into your project. Stitch exports are messy. Instead, use them as:

- **Visual reference** — keep the Stitch tab open while you code the React component
- **CSS values** — copy exact colors, padding, border-radius values from the export
- **Layout structure** — use the HTML structure as a guide for your JSX
- **Component ideas** — see how it organized sections into divs/containers

### Step 4 — What to Build Yourself in React
Take the visual design from Stitch and implement it properly in your React project (see Section 7 onwards). The Stitch export gives you the "what it looks like" — your React code gives it "how it works".

---

## 7. React Project Setup

Run these commands from inside your `SmartExam/` folder (same level as `Backend_API/`):

```bash
# Create the React app
npm create vite@latest Admin_Web_Panel -- --template react-ts
cd Admin_Web_Panel

# Install all dependencies
npm install

# Install Axios (HTTP requests to backend)
npm install axios

# Install SignalR client (real-time live monitor)
npm install @microsoft/signalr

# Install React Router (navigation between pages)
npm install react-router-dom

# Install React Query (server state management — makes API calls clean)
npm install @tanstack/react-query

# Install Tailwind CSS
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p

# Install Lucide icons (clean minimal icon set)
npm install lucide-react

# Install date utilities
npm install date-fns

# Run the dev server
npm run dev
# Should open at http://localhost:5173
```

### Configure Tailwind

Replace `tailwind.config.js` with:

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
        surface: '#FFFFFF',
        background: '#F8FAFC',
        border: '#E2E8F0',
        textPrimary: '#0F172A',
        textSecondary: '#64748B',
        textMuted: '#94A3B8',
        success: '#16A34A',
        warning: '#D97706',
        danger: '#DC2626',
      },
      fontFamily: {
        sans: ['Inter', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
```

Add to `src/index.css`:

```css
@tailwind base;
@tailwind components;
@tailwind utilities;

@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap');

body {
  font-family: 'Inter', sans-serif;
  background-color: #F8FAFC;
  color: #0F172A;
}
```

---

## 8. Folder Structure

Create this structure inside `Admin_Web_Panel/src/`:

```
src/
│
├── api/                    ← All backend API calls live here
│   ├── client.ts           ← Axios instance with base URL + auth header
│   ├── auth.api.ts         ← login, logout, me
│   ├── users.api.ts        ← getUsers, createUser, resetBinding, forceLogout
│   ├── exams.api.ts        ← getExams, getExamById, createExam, updateEligibility
│   ├── sessions.api.ts     ← startSession, submit, saveAnswer, getResults
│   └── monitoring.api.ts   ← getMonitoringEvents, sendWarning
│
├── components/             ← Reusable UI components
│   ├── layout/
│   │   ├── AdminSidebar.tsx
│   │   ├── TeacherSidebar.tsx
│   │   ├── Navbar.tsx
│   │   └── AppLayout.tsx    ← Wraps sidebar + navbar + children
│   ├── ui/
│   │   ├── Badge.tsx        ← Status badges (Active, Submitted, etc.)
│   │   ├── Button.tsx
│   │   ├── Card.tsx
│   │   ├── Input.tsx
│   │   ├── Modal.tsx
│   │   ├── Table.tsx
│   │   └── LoadingSpinner.tsx
│   └── exam/
│       ├── StudentTile.tsx  ← Live monitor student card
│       └── ViolationBadge.tsx
│
├── pages/                  ← One file per screen
│   ├── auth/
│   │   └── LoginPage.tsx
│   ├── admin/
│   │   ├── DashboardPage.tsx
│   │   ├── UsersPage.tsx
│   │   ├── DeviceBindingsPage.tsx
│   │   ├── LabsPage.tsx
│   │   └── AuditLogsPage.tsx
│   └── teacher/
│       ├── TeacherDashboard.tsx
│       ├── CreateExamPage.tsx
│       ├── LiveMonitorPage.tsx
│       ├── ResultsPage.tsx
│       └── EligibilityPage.tsx
│
├── context/
│   ├── AuthContext.tsx      ← Stores logged-in user, token, role
│   └── SignalRContext.tsx   ← SignalR connection for live monitor
│
├── hooks/
│   ├── useAuth.ts           ← useContext(AuthContext) shortcut
│   └── useSignalR.ts        ← Subscribe to monitoring events
│
├── types/
│   └── index.ts             ← All TypeScript interfaces matching backend DTOs
│
├── utils/
│   ├── formatters.ts        ← Date formatting, time display helpers
│   └── constants.ts         ← API base URL, role names, etc.
│
├── App.tsx                  ← Router setup
└── main.tsx                 ← Entry point
```

---

## 9. Backend Connection Layer

### `src/utils/constants.ts`

```typescript
export const API_BASE_URL = 'http://localhost:5000/api';
export const SIGNALR_HUB_URL = 'http://localhost:5000/hubs/monitoring';

export const ROLES = {
  SUPER_ADMIN: 'SuperAdmin',
  ADMIN: 'Admin',
  TEACHER: 'Teacher',
  STUDENT: 'Student',
} as const;
```

---

### `src/types/index.ts`

```typescript
// ── Auth ──────────────────────────────────────────────────────────────────────
export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  userId: string;
  name: string;
  email: string;
  role: 'SuperAdmin' | 'Admin' | 'Teacher' | 'Student';
  deviceBound: boolean;
}

export interface AuthUser {
  userId: string;
  name: string;
  email: string;
  role: LoginResponse['role'];
}

// ── Users ─────────────────────────────────────────────────────────────────────
export interface User {
  userId: string;
  name: string;
  email: string;
  role: LoginResponse['role'];
  isActive: boolean;
  createdAt: string;
  deviceBound: boolean;
  deviceRegisteredAt: string | null;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: LoginResponse['role'];
}

// ── Exams ─────────────────────────────────────────────────────────────────────
export type ExamStatus = 'Scheduled' | 'Active' | 'Ended';
export type QuestionType = 'Coding' | 'Theory';

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
  examId: string;
  userId: string;
  studentName: string;
  workstationNumber: string | null;
  isEligible: boolean;
  eligibilityNote: string | null;
}

// ── Sessions & Monitoring ─────────────────────────────────────────────────────
export type SessionStatus = 'InProgress' | 'Submitted' | 'ForceSubmitted' | 'TimedOut';
export type MonitoringEventType = 'Heartbeat' | 'WindowFocus' | 'ProcessList' | 'Violation';

export interface ExamSession {
  sessionId: string;
  examId: string;
  userId: string;
  startedAt: string;
  submittedAt: string | null;
  status: SessionStatus;
}

export interface MonitoringEvent {
  eventId: string;
  examSessionId: string;
  eventType: MonitoringEventType;
  payload: string;
  recordedAt: string;
}

// ── Live Monitor (SignalR payload shapes) ────────────────────────────────────
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
  status: 'Normal' | 'Warning' | 'Violation';
}

// ── AI Grading & Results ──────────────────────────────────────────────────────
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
  studentA: string;
  studentB: string;
  similarityScore: number;
  matchingSegments: string[];
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

// Attach JWT token to every request automatically
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('smartexam_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Handle 401 globally — redirect to login if token expired
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
  login: async (data: LoginRequest): Promise<LoginResponse> => {
    const res = await apiClient.post<LoginResponse>('/auth/login', data);
    return res.data;
  },

  logout: async (): Promise<void> => {
    await apiClient.post('/auth/logout');
  },

  me: async (): Promise<AuthUser> => {
    const res = await apiClient.get<AuthUser>('/auth/me');
    return res.data;
  },
};
```

---

### `src/api/users.api.ts`

```typescript
import apiClient from './client';
import type { User, CreateUserRequest } from '../types';

export const usersApi = {
  getAll: async (role?: string): Promise<User[]> => {
    const res = await apiClient.get<User[]>('/users', {
      params: role ? { role } : undefined,
    });
    return res.data;
  },

  getById: async (id: string): Promise<User> => {
    const res = await apiClient.get<User>(`/users/${id}`);
    return res.data;
  },

  create: async (data: CreateUserRequest): Promise<User> => {
    const res = await apiClient.post<User>('/users', data);
    return res.data;
  },

  deactivate: async (id: string): Promise<void> => {
    await apiClient.patch(`/users/${id}/deactivate`);
  },

  resetDeviceBinding: async (id: string): Promise<void> => {
    await apiClient.delete(`/users/${id}/device-binding`);
  },

  forceLogout: async (id: string): Promise<void> => {
    await apiClient.post(`/users/${id}/force-logout`);
  },
};
```

---

### `src/api/exams.api.ts`

```typescript
import apiClient from './client';
import type { Exam, ExamAssignment } from '../types';

export const examsApi = {
  getAll: async (): Promise<Exam[]> => {
    const res = await apiClient.get<Exam[]>('/exams');
    return res.data;
  },

  getById: async (id: string) => {
    const res = await apiClient.get(`/exams/${id}`);
    return res.data;
  },

  updateEligibility: async (
    examId: string,
    assignments: { userId: string; isEligible: boolean; note?: string }[]
  ): Promise<void> => {
    await apiClient.put(`/exams/${examId}/eligibility`, { assignments });
  },

  getAssignments: async (examId: string): Promise<ExamAssignment[]> => {
    const res = await apiClient.get<ExamAssignment[]>(`/exams/${examId}/assignments`);
    return res.data;
  },

  getResults: async (examId: string) => {
    const res = await apiClient.get(`/exams/${examId}/results`);
    return res.data;
  },

  overrideGrade: async (
    answerId: string,
    finalMarks: number,
    note: string
  ): Promise<void> => {
    await apiClient.post(`/answers/${answerId}/override`, { finalMarks, note });
  },
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

  // Restore session from localStorage on app load
  useEffect(() => {
    const savedToken = localStorage.getItem('smartexam_token');
    const savedUser = localStorage.getItem('smartexam_user');
    if (savedToken && savedUser) {
      setToken(savedToken);
      setUser(JSON.parse(savedUser));
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
  };

  return (
    <AuthContext.Provider value={{ user, token, login, logout, isLoading }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
```

---

## 10. Page-by-Page Backend Wiring

### Login Page — `pages/auth/LoginPage.tsx`

```
API call:  POST /api/auth/login
On success:
  1. Save token and user to AuthContext (calls context.login())
  2. Redirect based on role:
     - Admin / SuperAdmin → /admin/dashboard
     - Teacher → /teacher/dashboard
Error handling:
  - 401 → show "Invalid email or password"
  - 400 → show "HWID required" (should not happen on web panel)
  - Network error → show "Cannot connect to server"
```

---

### Admin — Users Page — `pages/admin/UsersPage.tsx`

```
On mount:      GET /api/users               → populate table
Tab change:    GET /api/users?role=Student   → filter by role
Search:        client-side filter on fetched data (no separate API call needed)
Add User:      POST /api/users              → refresh list on success
Deactivate:    PATCH /api/users/{id}/deactivate → update row in UI
Three-dot menu:
  Reset Binding  → DELETE /api/users/{id}/device-binding
  Force Logout   → POST /api/users/{id}/force-logout
CSV Upload:
  Read file client-side, parse rows, call POST /api/users for each row
  Show progress bar and success/error count
```

---

### Admin — Device Bindings Page — `pages/admin/DeviceBindingsPage.tsx`

```
On mount:      GET /api/users?role=Student  → fetch all students with deviceBound field
Filter toggle: client-side filter to show unbound only
Reset Binding: DELETE /api/users/{id}/device-binding
               → show confirmation modal first
               → on confirm, call API, update table row immediately
Force Logout:  POST /api/users/{id}/force-logout
               → show toast "Sessions revoked"
```

---

### Admin — Labs Page — `pages/admin/LabsPage.tsx`

```
On mount:      GET /api/labs                → fetch lab list with workstations
Add Lab:       POST /api/labs               → { name, location }
Add Workstation: POST /api/labs/{labId}/workstations → { machineNumber, ipAddress }
Delete:        DELETE /api/workstations/{id}
```

> Note: You will need to add these endpoints to the backend. Add a `LabsController` with these routes when you reach this page.

---

### Teacher — Create Exam Page — `pages/teacher/CreateExamPage.tsx`

```
Step 1 (Basic Info):
  GET /api/sections           → populate Section dropdown
  Store form state locally, no API call yet

Step 2 (Questions):
  Store questions locally in state
  No API call yet

Step 3 (Settings):
  Store settings locally

Step 4 (Students):
  GET /api/sections/{sectionId}/students  → show students to assign
  Toggle eligibility locally

Final Submit button:
  POST /api/exams  with full payload:
  {
    sectionId, title, startTime, durationMinutes,
    allowedApps, aiEvaluationEnabled, plagiarismThreshold,
    questions: [{ type, bodyText, marks, orderIndex, testCases: [...] }],
    assignments: [{ userId, isEligible }]
  }
  On success → redirect to /teacher/exams
```

> Note: You will need a `POST /api/exams` endpoint on the backend that accepts this full nested payload and creates the exam + questions + assignments in one transaction.

---

### Teacher — Live Monitor Page — `pages/teacher/LiveMonitorPage.tsx`

```
On mount:
  1. GET /api/exams?status=Active   → get the active exam
  2. GET /api/exams/{examId}/live-status  → initial snapshot of all student statuses
  3. Connect to SignalR hub (see Section 12)

Real-time updates via SignalR:
  Listen for "StudentHeartbeat"  → update that student's tile lastHeartbeat
  Listen for "WindowFocusChange" → update activeWindow on tile
  Listen for "ViolationEvent"    → increment violationCount, change tile border to red
  Listen for "StudentSubmitted"  → update tile status to Submitted

Actions:
  "Force Submit All"   → POST /api/exams/{examId}/force-submit-all
  Click student tile   → open detail drawer, GET /api/exams/sessions/{sessionId}/events
  "Send Warning"       → POST /api/exams/sessions/{sessionId}/send-warning  { message }
  "Force Submit" (one) → POST /api/exams/sessions/{sessionId}/force-submit
```

---

### Teacher — Results & Grading Page — `pages/teacher/ResultsPage.tsx`

```
On mount:
  GET /api/exams/{examId}/results
  Returns: array of { student, answers: [{ question, answerText, aiGrading, teacherOverride }] }

Expand a student:
  Render each answer with AI suggested marks and justification

Override grade:
  POST /api/answers/{answerId}/override  { finalMarks, note }
  On success → update the marks shown in UI optimistically

Plagiarism tab:
  GET /api/exams/{examId}/plagiarism
  Returns: array of flagged pairs with similarity scores
  Click "View Comparison" → render side-by-side diff
```

---

### Teacher — Eligibility Page — `pages/teacher/EligibilityPage.tsx`

```
On mount:
  GET /api/exams  → populate exam dropdown
  On exam selected:
  GET /api/exams/{examId}/assignments  → show students with isEligible + note

Toggle eligibility:
  Store changes in local state (do NOT call API on each toggle)

Save button:
  PUT /api/exams/{examId}/eligibility
  Body: { assignments: [{ userId, isEligible, note }] }
  On success → show toast "Eligibility saved"
```

---

## 11. Auth Flow & Route Protection

### `src/App.tsx`

```tsx
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import LoginPage from './pages/auth/LoginPage';
import DashboardPage from './pages/admin/DashboardPage';
import UsersPage from './pages/admin/UsersPage';
import DeviceBindingsPage from './pages/admin/DeviceBindingsPage';
import LabsPage from './pages/admin/LabsPage';
import AuditLogsPage from './pages/admin/AuditLogsPage';
import TeacherDashboard from './pages/teacher/TeacherDashboard';
import CreateExamPage from './pages/teacher/CreateExamPage';
import LiveMonitorPage from './pages/teacher/LiveMonitorPage';
import ResultsPage from './pages/teacher/ResultsPage';
import EligibilityPage from './pages/teacher/EligibilityPage';

// Protects routes — redirects to login if not authenticated
function PrivateRoute({ children, allowedRoles }: { children: React.ReactNode; allowedRoles: string[] }) {
  const { user, isLoading } = useAuth();
  if (isLoading) return <div>Loading...</div>;
  if (!user) return <Navigate to="/login" replace />;
  if (!allowedRoles.includes(user.role)) return <Navigate to="/login" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />

          {/* Admin routes */}
          <Route path="/admin/dashboard" element={<PrivateRoute allowedRoles={['Admin','SuperAdmin']}><DashboardPage /></PrivateRoute>} />
          <Route path="/admin/users" element={<PrivateRoute allowedRoles={['Admin','SuperAdmin']}><UsersPage /></PrivateRoute>} />
          <Route path="/admin/device-bindings" element={<PrivateRoute allowedRoles={['Admin','SuperAdmin']}><DeviceBindingsPage /></PrivateRoute>} />
          <Route path="/admin/labs" element={<PrivateRoute allowedRoles={['Admin','SuperAdmin']}><LabsPage /></PrivateRoute>} />
          <Route path="/admin/audit-logs" element={<PrivateRoute allowedRoles={['Admin','SuperAdmin']}><AuditLogsPage /></PrivateRoute>} />

          {/* Teacher routes */}
          <Route path="/teacher/dashboard" element={<PrivateRoute allowedRoles={['Teacher']}><TeacherDashboard /></PrivateRoute>} />
          <Route path="/teacher/create-exam" element={<PrivateRoute allowedRoles={['Teacher']}><CreateExamPage /></PrivateRoute>} />
          <Route path="/teacher/live-monitor" element={<PrivateRoute allowedRoles={['Teacher']}><LiveMonitorPage /></PrivateRoute>} />
          <Route path="/teacher/results/:examId" element={<PrivateRoute allowedRoles={['Teacher']}><ResultsPage /></PrivateRoute>} />
          <Route path="/teacher/eligibility" element={<PrivateRoute allowedRoles={['Teacher']}><EligibilityPage /></PrivateRoute>} />

          {/* Default redirect */}
          <Route path="/" element={<Navigate to="/login" replace />} />
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
```

---

## 12. Real-Time SignalR Connection

This powers the live proctoring dashboard. Add this **after** your backend adds SignalR hub support.

### `src/context/SignalRContext.tsx`

```tsx
import { createContext, useContext, useEffect, useRef, useState, ReactNode } from 'react';
import * as signalR from '@microsoft/signalr';
import { SIGNALR_HUB_URL } from '../utils/constants';
import { useAuth } from './AuthContext';
import type { StudentLiveStatus } from '../types';

interface SignalRContextType {
  studentStatuses: Map<string, StudentLiveStatus>;
  isConnected: boolean;
}

const SignalRContext = createContext<SignalRContextType | null>(null);

export function SignalRProvider({ children }: { children: ReactNode }) {
  const { token } = useAuth();
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [studentStatuses, setStudentStatuses] = useState<Map<string, StudentLiveStatus>>(new Map());

  useEffect(() => {
    if (!token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_HUB_URL, {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect()
      .build();

    // Listen for heartbeat updates from students
    connection.on('StudentHeartbeat', (data: { userId: string; timestamp: string; activeWindow: string }) => {
      setStudentStatuses(prev => {
        const updated = new Map(prev);
        const existing = updated.get(data.userId);
        if (existing) {
          updated.set(data.userId, {
            ...existing,
            lastHeartbeat: data.timestamp,
            activeWindow: data.activeWindow,
          });
        }
        return updated;
      });
    });

    // Listen for violation events
    connection.on('ViolationEvent', (data: { userId: string; violationType: string; details: string }) => {
      setStudentStatuses(prev => {
        const updated = new Map(prev);
        const existing = updated.get(data.userId);
        if (existing) {
          updated.set(data.userId, {
            ...existing,
            violationCount: existing.violationCount + 1,
            status: 'Violation',
          });
        }
        return updated;
      });
    });

    // Listen for student submission
    connection.on('StudentSubmitted', (data: { userId: string }) => {
      setStudentStatuses(prev => {
        const updated = new Map(prev);
        const existing = updated.get(data.userId);
        if (existing) {
          updated.set(data.userId, { ...existing, status: 'Normal' });
        }
        return updated;
      });
    });

    connection.start()
      .then(() => {
        setIsConnected(true);
        connectionRef.current = connection;
      })
      .catch(err => console.error('SignalR connection failed:', err));

    return () => {
      connection.stop();
      setIsConnected(false);
    };
  }, [token]);

  return (
    <SignalRContext.Provider value={{ studentStatuses, isConnected }}>
      {children}
    </SignalRContext.Provider>
  );
}

export function useSignalR() {
  const ctx = useContext(SignalRContext);
  if (!ctx) throw new Error('useSignalR must be inside SignalRProvider');
  return ctx;
}
```

Wrap the Live Monitor page (and only that page) with `<SignalRProvider>` to avoid connecting when not needed.

---

## 13. Build Order

Build pages in this exact order. Do not skip ahead.

```
Week 1 — Core shell
  ✓ Project setup (Section 7)
  ✓ Design system configured (Tailwind tokens)
  ✓ AuthContext + apiClient + types (Sections 8–9)
  ✓ AppLayout component (sidebar + navbar shell, no real nav yet)
  ✓ LoginPage → connects to POST /api/auth/login → redirects by role

Week 2 — Admin pages
  ✓ Admin DashboardPage (static stats for now, real data later)
  ✓ UsersPage → GET /api/users → table renders
  ✓ Add User modal → POST /api/users → row appears in table
  ✓ DeviceBindingsPage → reset binding works end-to-end

Week 3 — Teacher core
  ✓ TeacherDashboard (exam list from GET /api/exams)
  ✓ CreateExamPage (multi-step form → POST /api/exams)
  ✓ EligibilityPage → GET assignments → PUT eligibility

Week 4 — Live monitor + results
  ✓ LiveMonitorPage → student grid → SignalR connected
  ✓ ResultsPage → AI grades displayed → teacher override works

Stretch (if time permits)
  □ AuditLogsPage
  □ LabsPage
  □ CSV bulk upload
  □ Exam summary statistics chart
```

---

## Quick Reference — All API Endpoints Used by Frontend

| Page | Method | Endpoint |
|------|--------|----------|
| Login | POST | `/api/auth/login` |
| Logout | POST | `/api/auth/logout` |
| Get current user | GET | `/api/auth/me` |
| Get all users | GET | `/api/users?role=...` |
| Create user | POST | `/api/users` |
| Deactivate user | PATCH | `/api/users/{id}/deactivate` |
| Reset device binding | DELETE | `/api/users/{id}/device-binding` |
| Force logout user | POST | `/api/users/{id}/force-logout` |
| Get all exams | GET | `/api/exams` |
| Get exam detail | GET | `/api/exams/{id}` |
| Get exam assignments | GET | `/api/exams/{id}/assignments` |
| Update eligibility | PUT | `/api/exams/{id}/eligibility` |
| Get exam results | GET | `/api/exams/{id}/results` |
| Get plagiarism flags | GET | `/api/exams/{id}/plagiarism` |
| Override grade | POST | `/api/answers/{id}/override` |
| Force submit all | POST | `/api/exams/{id}/force-submit-all` |
| Get session events | GET | `/api/exams/sessions/{id}/events` |
| Send warning to student | POST | `/api/exams/sessions/{id}/send-warning` |
| Force submit one student | POST | `/api/exams/sessions/{id}/force-submit` |
| Get labs | GET | `/api/labs` |
| Create lab | POST | `/api/labs` |
| Add workstation | POST | `/api/labs/{id}/workstations` |
| **SignalR Hub** | WS | `/hubs/monitoring` |

> Endpoints marked in the page wiring section that do not yet exist in the backend need to be added before that page can be fully connected. Add them one at a time as you reach each page.
