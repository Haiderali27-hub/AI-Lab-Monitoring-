# FYP-1 Module Status — Single Source of Truth

> **Purpose:** Tracks the 6 FYP-1 modules against the thesis (Haider_Zain_Report). Every work session reads this file first and updates it after changes. Do NOT rely on memory — this doc is authoritative.
>
> ## 🎉 STATUS: ALL 6 FYP-1 MODULES COMPLETE (2026-07-03)
> **99/99 xUnit tests · live smoke suites 16/16 (M8), 21/21 (M1+M7), 18/18 (M9+M10) · demo flow works end-to-end:** admin creates users/labs/seats → teacher creates exam (MCQ/coding/theory + instructions) → assigns eligible students + seats → student logs in (HWID-bound) → checks in + verifies identity → starts exam → teacher watches live grid, gets focus-loss violations, extends time, force-submits — all audited.
>
> **FYP-1 scope (6 modules):** 1 (Auth/HWID), 7 (User/Role), 8 (Exam Creation), 9 (Eligibility/Access), 3 (Student Exam Dashboard), 10 (Live Proctoring @ heartbeat level).
> **FYP-2 deferred:** 2 (Pre-Exam Verify/Mock), 4 (Secure Environment/lockdown), 5 (screenshot/webcam capture), 6 (Submission/encrypted backup), 11 (real AI eval/Docker), 12 (Audit UI/QuestPDF full reporting).
>
> **Off-thesis modules (built but NOT in thesis — hide for FYP-1 demo):** Notifications/Announcements, Student Performance Portal, Analytics/Reports pages, Audit Logs page (hardcoded stub), Labs page (hardcoded stub).

**Legend:** ✅ working · ⚠️ partial/broken · ❌ missing · 🔴 demo blocker

**Paths:**
- Backend: `SmartExam_Root/Backend_API` (ASP.NET Core 9, Neon PostgreSQL, JWT, SignalR)
- Web panel: `SmartExam_Root/Admin_Web_Panel` (React 19 + TS, port 5173) — v2_redesign is UI-only playground, do functional work HERE
- Desktop: `SmartExam_Root/Student_Desktop_App` (WPF .NET 9)
- Backend tests: `SmartExam_Root/Backend_API.Tests` (xUnit, in-memory DB)

---

## Module 1 — Authentication & Device Binding (✅ COMPLETE for FYP-1, 2026-07-03)

| Feature (thesis) | Status | Notes |
|---|---|---|
| Email/password login, JWT, role-based access | ✅ | `AuthController` /login + /student-login |
| HWID capture (WMI CPU + board + MAC) | ✅ | `DeviceFingerprint.cs` (client); thesis wants BIOS+disk+board triple — ours is CPU+board+MAC (acceptable) |
| First-login bind, server-side mismatch → 401 DEVICE_MISMATCH | ✅ | `AuthController.cs` student-login |
| Admin reset binding | ✅ | `DELETE /api/users/{id}/device-binding` |
| **HWID hashed before storage** | ✅ | `HwidHelper.Hash` (SHA-256) — server hashes the raw fingerprint before store/compare; DB never holds raw device IDs. Seeders updated to store hashes |
| **Security audit logging** | ✅ | `AuditService` writes to `AuditLog`: LOGIN_SUCCESS/FAILED, LOGIN_DENIED_INACTIVE, DEVICE_BOUND, DEVICE_MISMATCH, LOGOUT, PASSWORD_CHANGED + all M7 admin actions. Viewable at `GET /api/audit-logs` (Admin only) |
| **Logout / force-logout / deactivation enforced** | ✅ | `Program.cs` `OnTokenValidated` rejects revoked-session JTIs AND inactive users → a still-unexpired token stops working immediately |
| **Change password** | ✅ | `POST /api/auth/change-password` (verify current, min-8 new); Navbar avatar → Change Password modal (all roles) |
| Token refresh | — | `/auth/refresh` still a stub — deliberately deferred to FYP-2 (needs refresh-token store/rotation); 8h access token covers a lab session |
| Biometric enrollment | — | Optional per thesis — FYP-2 |

**QA evidence (2026-07-03):** covered by 82/82 xUnit + 21/21 live smoke (see M7 below).

## Module 7 — User & Role Management (✅ COMPLETE for FYP-1, 2026-07-03)

| Feature (thesis) | Status | Notes |
|---|---|---|
| List/filter users by role | ✅ | `GET /api/users?role=` |
| Create user with role | ✅ | `POST /api/users` |
| Reset device binding / force logout | ✅ | UsersPage + DeviceBindingsPage wired; both now audited |
| **Edit user (name/email/role + optional password reset)** | ✅ | `PUT /api/users/{id}`; UsersPage slide-over reused for edit (was an `alert()` stub); email-uniqueness + no-self-role-change guards |
| **Delete user** | ✅ | `DELETE /api/users/{id}` with guards: self→400, section-owner→409, has-exam-history→409 (deactivate instead); cleans up bindings/sessions/assignments/enrollments. Delete action in row menu |
| **Deactivate + Reactivate** | ✅ | `PATCH .../deactivate` (also revokes live sessions) + `PATCH .../activate`; toggle in row menu; deactivation blocks login immediately |
| **Batch/CSV import** | ✅ | `POST /api/users/import`; Upload CSV button parses `name,email,password,role` header → import; returns created/skipped with reasons |
| **Invigilator role** | ✅ | Added to `UserRole` enum (4 roles now: Admin/Teacher/Invigilator/Student, + SuperAdmin); tab, badge, role dropdowns updated. Role stored as string so no migration needed |
| **Access logs** | ✅ | Every admin action audited (USER_CREATED/UPDATED/DELETED/ACTIVATED/DEACTIVATED, USERS_IMPORTED, DEVICE_BINDING_RESET, FORCE_LOGOUT) → `GET /api/audit-logs` |

**QA evidence (2026-07-03):** **82/82 xUnit** (17 new M1+M7 tests incl. negatives: revoked-token→401, deactivated-token→401, wrong-current-password→400, edit-to-dup-email→409, delete-self→400, delete-section-owner→409, import-as-teacher→403, audit-as-student→403). **Live smoke vs Neon: 21/21** (login audit, change-password round-trip, logout revocation, HWID hash + mismatch audit, Invigilator create, edit, deactivate/reactivate login gating, delete guards, batch import 2-created/1-skipped).

## Module 8 — Exam Creation & Configuration (✅ COMPLETE for FYP-1, 2026-07-03)

| Feature (thesis) | Status | Notes |
|---|---|---|
| Create exam: title, section, schedule, duration | ✅ | `POST /api/exams`, 4-step wizard `CreateExamPage.tsx` |
| Coding + Theory questions with marks | ✅ | `QuestionType` enum |
| **MCQ questions (options + correct answer)** | ✅ | `QuestionType.Mcq`; `Question.OptionsJson` + `CorrectOptionIndex`; wizard options editor w/ radio for correct; **auto-graded exactly on submit** (full marks / 0); **skipped by plagiarism detector** |
| **Reference key / sample solution per question** | ✅ | `Question.ReferenceAnswer` (Coding/Theory, optional); never sent to students |
| **Instructions** | ✅ | BUG FIXED: wizard now sends `instructions`; stored on `Exam.Instructions`; `/student/current` returns instructions + allowed-apps line → WPF dashboard shows them |
| Test cases (input/expected/hidden) | ✅ | Students only see non-hidden; **server now requires ≥1 test case per coding question + expected output on every test case** |
| **Edit exam** | ✅ | `PUT /api/exams/{id}` — Scheduled only (409 otherwise); replaces questions + assignments (keeps eligibility of retained students); wizard reused at `/teacher/exams/:examId/edit` w/ prefill; Edit button on teacher dashboard |
| **Delete exam** | ✅ | `DELETE /api/exams/{id}` — Scheduled only (409 otherwise); Delete button w/ confirm on teacher dashboard |
| Server-side validation | ✅ | ≥1 question; marks>0; body text required; MCQ ≥2 non-empty options + valid correct index + no test cases; coding needs test cases; threshold 0–100; invalid students/section rejected |
| Answer-key protection | ✅ | Students never receive `correctOptionIndex` / `referenceAnswer` (GetById strips them) |
| Allowed-apps whitelist (stored + echoed) | ✅ | Enforcement is client-side = FYP-2 Module 4 |
| Plagiarism threshold + AI-eval toggle | ✅ | Stored, used at submit |
| Proctoring-level config (screenshot interval etc.) | — | FYP-2 (depends on capture modules) |

**QA evidence (2026-07-03):** 65/65 xUnit tests pass (17 new Module 8 tests incl. negative cases: no questions→400, MCQ <2 options→400, bad correct index→400, MCQ w/ test cases→400, coding w/o test cases→400, threshold 150→400, student create/delete→403, edit/delete Active exam→409, nonexistent→404, student key-stripping, MCQ right/wrong grading). Live smoke vs Neon: 16/16 (the single "Dr. Ahmed" flag was a false alarm — the seeded teacher's real name IS "Dr. Ahmed"; dynamic lookup verified in tests). Migration `20260702184740_Module8ExamFields` applied to Neon (4 nullable columns, non-destructive).

## Module 9 — Eligibility & Access Control (✅ COMPLETE for FYP-1, 2026-07-03)

| Feature (thesis) | Status | Notes |
|---|---|---|
| Assign students at exam creation | ✅ | `ExamAssignment` rows |
| Eligibility toggle + note + mark-all + save | ✅ | EligibilityPage → `PUT /api/exams/{id}/eligibility`; **now audited (ELIGIBILITY_UPDATED)** per thesis FR9 |
| **Seating map (assign workstation/seat)** | ✅ | `PUT /api/exams/{examId}/assignments/{userId}/workstation`; EligibilityPage workstation dropdown (seat — lab — IP); unassign supported |
| **Seat-conflict detection** | ✅ | Same seat → second student in same exam → 409 (thesis UCT-04 exception flow) |
| **Labs & workstation inventory** | ✅ | New `LabsController`: `GET /api/labs` (staff), `POST /api/labs` + `POST /api/labs/{id}/workstations` (admin, dup machine# → 409); **LabsPage rewired from hardcoded stub to real API** with add-lab/add-workstation forms |
| **Workstation → IP mapping** | ✅ | IP registered per workstation, shown in seat picker. *Enforcement* (blocking login from wrong IP) deferred to FYP-2 with Module 4 lockdown |
| **Add/remove student on existing exam** | ✅ | `POST /api/exams/{id}/assignments` (dup → 409, non-student → 400) + `DELETE .../assignments/{userId}` (has-session → 409, history preserved); EligibilityPage "Add student…" dropdown + row remove button |
| Per-candidate time windows | — | FYP-2 (single exam window per thesis core flow) |

## Module 3 — Student Exam Dashboard, WPF (✅ COMPLETE for FYP-1, 2026-07-03)

| Feature (thesis) | Status | Notes |
|---|---|---|
| Assigned exam, status, lab, session message (real API) | ✅ | `GET /api/exams/student/current` |
| Live countdown timer | ✅ | 1s DispatcherTimer; **now resyncs from server clock on every heartbeat ack** (picks up teacher time extensions) |
| Custom title bar (min/close), Refresh, Logout | ✅ | — |
| **Start Exam button enables** | ✅ | 🔴 BLOCKER FIXED: new "PRE-EXAM CHECKS" panel with **Check In (attendance)** + **Verify Identity** buttons wired to the existing VM methods → `CanStartExam` becomes true → full student flow works end-to-end |
| Readiness indicators | ✅ | Attendance + Identity status tiles ("Not checked in"/"Checked in", "Not verified"/"Verified"); eligibility carried in status message |
| Instructions display | ✅ | Fixed via Module 8 (2026-07-03): real teacher instructions + allowed-apps line |
| Proctor name | ✅ | Fixed (2026-07-03): real section teacher via `Section.Teacher.Name` |
| Exam-taking runtime, camera-based identity | — | FYP-2 (Modules 4/6; identity check is an event check-in for FYP-1, camera in FYP-2 Module 2) |

## Module 10 — Live Proctoring Dashboard (✅ COMPLETE for FYP-1, 2026-07-03)

| Feature (thesis) | Status | Notes |
|---|---|---|
| Live SignalR grid, status colors (green/yellow/red) | ✅ | `LiveMonitorPage.tsx` |
| Active window, process list (top 12), progress, telemetry drawer | ✅ | 10s heartbeat from WPF client |
| Send warning / force-submit one / force-end all | ✅ | Hub methods + REST |
| **Violation pipeline is LIVE** | ✅ | WPF client now emits a `FocusLoss` violation on the transition out of the exam window (30s cooldown so it can't flood) → persisted + pushed to the teacher grid → card goes red. Was a dead pipeline |
| **Extend time** | ✅ | `POST /api/exams/{id}/extend-time` (1–180 min, Ended → 409); "Extend Time" button on LiveMonitorPage; `TimeExtended` hub event resyncs other proctors; **student countdown resyncs via heartbeat ack `remainingSeconds`** |
| **Broadcast scoping** | ✅ | Heartbeats/violations broadcast to `exam:{examId}` group only (was `Clients.All`); LiveMonitorPage joins via `JoinExamGroup` (staff-only) |
| **Hub authorization** | ✅ | `JoinExamGroup`/`SendWarning`/`ForceSubmitSession` require staff role; force-submit additionally checks the teacher owns the exam's section (Admin/SuperAdmin exempt) |
| Webcam feed / screenshot thumbnails | — | FYP-2 Module 5 (capture) |
| Pause/resume timer, remote unlock, terminate session | — | FYP-2 (depend on Module 4 lockdown client) |
| Unauthorized-app *detection* | — | Process list is streamed for teacher review; automatic blacklist detection lands with FYP-2 Module 4 enforcement |

---

## Work log

| Date | Module | Change |
|---|---|---|
| 2026-07-02 | — | Doc created from 3-way code audit (backend/web/desktop) |
| 2026-07-03 | 8 | COMPLETE: MCQ type (create+grade+plagiarism-skip), reference answers, instructions bug fixed end-to-end, edit exam (PUT + wizard prefill), delete exam, hardened validation, student answer-key stripping. Migration `Module8ExamFields` on Neon. 65/65 xUnit, 16/16 live smoke. |
| 2026-07-03 | 3 (partial) | Side-fixes landed with M8: student dashboard now shows real instructions + real proctor name (was hardcoded "Dr. Ahmed"). Start-Exam blocker still OPEN. |
| 2026-07-03 | 1 + 7 | COMPLETE: HWID hashing (SHA-256), full audit trail (`AuditService` + `GET /api/audit-logs`), token-revocation + deactivation enforcement (`OnTokenValidated`), change-password (API + Navbar modal). User edit/delete/activate/reactivate, CSV batch import, Invigilator role. No schema migration (role stored as string). 82/82 xUnit, 21/21 live smoke. Off-thesis note: refresh-token + biometric deferred to FYP-2. |
| 2026-07-03 | Bug fixes + polish | Fixed: (1) WPF now detects force-logout/deactivation (session-watch poll + heartbeat 401 → kicks to login); (2) student app now reliably shows the assigned exam (student/current + start now pick the nearest-open exam among multiple assignments); (3) student/current splits real **lab name / seat / allowed-apps** into separate fields (seat was leaking into LabName). Seeder now **only seeds an empty DB** (your manual data persists across restarts); added `POST /api/admin/reset-demo-data` (admin) for clean demos; seats renamed **R1C1/R1C2**; demo records labelled "(Demo)"; removed the pre-baked fake device binding. Web: Audit Logs page wired to real `/api/audit-logs` (was hardcoded stub); fake analytics leaderboard + plagiarism code-highlight replaced with honest FYP-2 notes. **Student desktop UI fully redesigned** (3-pane exam console: gradient exam card, live status rail w/ lab/seat/monitoring, pre-exam checklist with tick states, **proctor-messages panel** polling teacher warnings, connection pill, low-time red countdown, allowed-app chips). 99/99 xUnit + 31/31 negative live smoke. NOTE: restart `npm run dev` to pick up the rewritten Labs page. |
| 2026-07-03 | 9 + 3 + 10 | COMPLETE — ALL 6 FYP-1 MODULES DONE. M9: LabsController (labs/workstations CRUD), seat assignment w/ conflict 409, add/remove student on existing exam, LabsPage rewired from stub, EligibilityPage seat dropdown + roster controls. M3: 🔴 Start-Exam blocker fixed (Check In + Verify Identity buttons + readiness tiles). M10: WPF emits FocusLoss violations (transition-based, 30s cooldown), broadcasts scoped to `exam:{id}` groups, hub role+ownership checks, extend-time (REST + button + TimeExtended event), student countdown resyncs via heartbeat ack. No migration needed. 99/99 xUnit, 18/18 live smoke. |
