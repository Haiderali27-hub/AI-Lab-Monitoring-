# SmartExam: Phase 2 Implementation Roadmap

This document outlines the step-by-step plan to transition SmartExam from a functional prototype to a production-ready Examination Management System.

---

## Phase 1: Exam Lifecycle Completion (High Priority)
*Goal: Move beyond simple Create/Update to a full administrative lifecycle (Cancel, Archive, Delete).*

### Step 1.1: Backend - Lifecycle API Endpoints
- [ ] **Implement `DeleteExam`**: Add an endpoint in `ExamController.cs` to remove an exam. Ensure it handles cascading deletions for `ExamAssignments` or prevents deletion if `ExamSessions` exist.
- [ ] **Implement `CancelExam`**: Add a PATCH/PUT endpoint to set an exam status to "Cancelled". This should immediately invalidate active student sessions.
- [ ] **Implement `ArchiveExam`**: Add logic to "Archive" completed exams to keep the main "Live/Upcoming" list clean.

### Step 1.2: Frontend - Exam Management UI
- [ ] **Update Exam List**: Add action buttons (Cancel, Archive, Delete) to the exam cards/rows.
- [ ] **Confirmation Dialogs**: Implement modal confirmations for destructive actions (Delete/Cancel).
- [ ] **Status Filtering**: Add tabs or filters to the Exam Dashboard (Active, Scheduled, Completed, Archived).

---

## Phase 2: Enhanced Role & User Administration (Medium Priority)
*Goal: Transition from fixed-role creation to a flexible user management system.*

### Step 2.1: Backend - Role & Profile Management
- [ ] **Update User Endpoint**: Add an endpoint to update existing user details (Username, Email, Role).
- [ ] **Role Transition Logic**: Ensure that changing a role (e.g., Student to Teacher) correctly updates associated data (e.g., removing section enrollments).
- [ ] **Permission Matrix (Optional)**: If needed, implement a simple `UserPermission` table for more granular control (e.g., "Can Create Exams" vs "Can only Proctor").

### Step 2.2: Frontend - User Management UI
- [ ] **User Edit Modal**: In `AdminDashboard.tsx`, replace the simple "Create" form with an "Edit User" modal.
- [ ] **Role Selector**: Allow Admins to change roles of existing users.
- [ ] **Search & Pagination**: Add search bars and pagination to the Student/Teacher tables for better scalability.

---

## Phase 3: Runtime Validation & Performance (Low Priority)
*Goal: Ensure the system works reliably under load and against the live database.*

### Step 3.1: Database Integrity & Constraints
- [ ] **Audit DB Triggers**: Ensure PostgreSQL constraints (Foreign Keys) are properly enforcing data integrity during deletes.
- [ ] **Indexing**: Add indexes to `ExamAssignments` and `MonitoringEvents` for faster "Live Roster" queries.

### Step 3.2: End-to-End Flow Validation
- [ ] **Docker Test Run**: Execute a full "Exam Day" scenario using the `docker-compose.postgres.yml` environment.
- [ ] **Mock Student Load**: Simulate 50+ students sending heartbeats to verify the `MonitoringController` performance.

---

## Phase 4: UI/UX Polish & "WOW" Factor
*Goal: Make the interface feel premium and professional.*

### Step 4.1: Micro-Animations & Feedback
- [ ] **Success Toasts**: Replace standard alerts with floating toast notifications for actions like "Exam Created" or "User Deleted".
- [ ] **Skeleton Loaders**: Add shimmer/skeleton effects while dashboards are fetching data.
- [ ] **Real-time Updates**: Integrate SignalR (or polling optimization) for the "Live Roster" to make it feel instantaneous.

---

## Immediate Next Steps
1. **Start with Step 1.1**: Open `ExamController.cs` and implement the `Delete` and `Cancel` logic.
2. **Sync Frontend**: Immediately update the Exam Dashboard to reflect these new capabilities.
