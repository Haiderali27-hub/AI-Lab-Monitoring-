# SmartExam — Implementation Plan (FYP-1 gaps → FYP-2 completion)

**Baseline:** branch `fyp1-security-hardening` (commit `768df71`), 105/105 backend tests passing.
**Scope source:** thesis `important docs/fyp_doc_3chatp/Chapters/Chapter-1-8.tex` (12 modules, 8 sprints), `Chapter-2-2.tex` (FR1–FR14, NFRs), `Chapter-3-6.tex` (architecture).
**Test IDs** referenced below are defined in [`TEST_CASES.md`](TEST_CASES.md).

---

## 0. Where we are

| # | Module | Thesis sprint | Status | Est. done |
|---|--------|---------------|--------|-----------|
| 1 | Authentication & Device Binding | S2 | Mostly complete | 80% |
| 2 | Pre-Exam Verification & Mock Test | S4 | **Not started** | 0% |
| 3 | Student Exam Dashboard | S4 | Mostly complete | 80% |
| 4 | Secure Exam Environment (lockdown) | S4 | **Implemented, lab validation pending** (see §3.1 status) | 75% |
| 5 | Live Monitoring Client Service | S5 | Barely started | 20% |
| 6 | Submission & Backup | S6 | Partial | 50% |
| 7 | User & Role Management | S2 | Mostly complete | 85% |
| 8 | Exam Creation & Configuration | S3 | Mostly complete | 80% |
| 9 | Eligibility & Access Control | S3 | Partial | 55% |
| 10 | Live Proctoring Dashboard | S5 | Partial | 60% |
| 11 | AI-Assisted Evaluation | S6 | Barely started | 15% |
| 12 | Logs, Analytics & Audit Trail | S7 | Partial | 55% |

**Overall ≈ 50% of thesis scope.** Modules 2 and 4 are scheduled for FYP-1 in the thesis and are the most visible gaps.

---

## 1. Phasing

| Phase | Focus | Modules | Duration | Exit criteria |
|-------|-------|---------|----------|---------------|
| **P0** | Test harness & hygiene | — | 3 days | E2E suite runs headless against an in-memory backend with one command; CI green |
| **P1** | Close FYP-1 gaps | 4, 2, 9, 3 | 3–4 weeks | Lockdown demo-able; readiness check gates exam start; seat + attendance enforced |
| **P2** | Monitoring & resilience | 5, 6, 10 | 4 weeks | Screenshots on live monitor; offline exam survives network loss; incidents workflow |
| **P3** | Evaluation & evidence | 11, 12 | 4 weeks | Code is compiled and test-case scored in a sandbox; tamper-evident audit; full reports |
| **P4** | Leftovers & polish | 1, 7, 8, docs | 2 weeks | Refresh tokens; invigilator UI; thesis chapters 4.3–4.5, 7, 8, 10 written |

Suggested split for a 3-person team: **Desktop (WPF)** owner → 4, 2, 5, 6-client; **Backend** owner → 9, 3, 6-server, 11, 12; **Web** owner → 10, 12-UI, 7, test harness. Each phase ends with a demo + test run recorded in Chapter 7.

Definition of Done for every task: code + tests listed in its row passing in CI, audit events emitted where relevant, thesis section updated.

---

## 2. Phase P0 — Test harness & hygiene (do first)

| Task | Detail | Files |
|------|--------|-------|
| P0.1 E2E backend mode | `E2E:InMemory=true` → backend uses EF InMemory (`SmartExamE2E`) and runs `DbSeeder`. E2E tests can never touch Neon. | `Backend_API/Program.cs` |
| P0.2 Playwright starts both servers | `webServer` array: backend (`dotnet run --urls http://localhost:5050`, env `E2E__InMemory=true`) + Vite. `reuseExistingServer` only when `PW_REUSE=1`. | `Admin_Web_Panel/playwright.config.ts` |
| P0.3 Student client simulator | Playwright `request`-based fixture that does what the WPF app does: `student-login` with a fake HWID, `start-session`, `save-answer`, heartbeat, violation events, submit. Lets headless tests cover the full monitoring path. | `tests/e2e/support/studentClient.ts` |
| P0.4 API test layer | Headless API specs (`tests/api/*.spec.ts`) for security & business rules that need no UI. | `tests/api/` |
| P0.5 Stable selectors | Add `data-testid` to tiles, tables, dialogs used by tests (currently only 2 in the codebase). | web `src/pages/**` |
| P0.6 Scripts | `test:e2e`, `test:api`, `test:all`, `test:report`. | `package.json` |
| P0.7 CI | GitHub Actions: `dotnet test` + Playwright (Linux, headless, chromium) on every PR; upload HTML report artifact. | `.github/workflows/ci.yml` |
| P0.8 Desktop test project | `Student_Desktop_App.Tests` (xUnit, net9.0-windows) for pure logic classes introduced in P1/P2. | new project |
| P0.9 Hygiene leftovers | ✅ Deleted `Admin_Web_Panel_v2_redesign` (stale copy of v1); ✅ MailKit 4.7.1 → 4.18.1 (GHSA-9j88-vvj5-vhgr). Neon credential rotation: owner decided not needed. | — |

---

## 3. Phase P1 — Close the FYP-1 gaps

### 3.1 Module 4 — Secure Exam Environment (thesis FR4, `Ch-1-5`, `Ch-3-6`)

**Goal:** while an exam is in progress the student can only use the exam window and whitelisted tools; every escape attempt is blocked or reported.

**Design — Student_Desktop_App**

| Component | Responsibility | Notes |
|-----------|----------------|-------|
| `Core/Lockdown/LockdownPolicy.cs` | **Pure** decision logic: is key combo blocked? is process allowed? is environment acceptable? | 100% unit-testable, no Win32 |
| `Services/Lockdown/KeyboardHookService.cs` | `SetWindowsHookEx(WH_KEYBOARD_LL)`; swallows Alt+Tab, Alt+Esc, Alt+F4, Win (L/R), Ctrl+Esc, Ctrl+Shift+Esc, PrintScreen; reports each attempt | Ctrl+Alt+Del cannot be blocked by design — detected via focus loss |
| `Services/Lockdown/ProcessGuardService.cs` | Every 2 s: enumerate processes, compare with `Exam.AllowedApps` + system whitelist; `Enforce` mode kills, `Report` mode only reports | Mode from exam config |
| `Services/Lockdown/ForegroundGuardService.cs` | `GetForegroundWindow` → `GetWindowThreadProcessId` → compare **PID** to own process / allowed apps | Replaces title-contains-"Exam" check in `LiveMonitoringService.cs:67` |
| `Services/Lockdown/ClipboardGuardService.cs` | `AddClipboardFormatListener`; clear clipboard on change; report copy attempts from outside apps | |
| `Services/Lockdown/DeviceGuardService.cs` | WMI `Win32_VolumeChangeEvent` (USB insert), `Screen.AllScreens.Length > 1`, `SystemInformation.TerminalServerSession` (RDP), basic VM detection | Report as violation |
| `Services/Lockdown/TaskManagerPolicy.cs` | Set `HKCU\…\Policies\System\DisableTaskMgr=1` during exam, **always** restore on exit + on next app start (crash recovery marker file) | Optional, behind config |
| `Views/ExamWindow` | `WindowStyle=None`, `Topmost`, `WindowState=Maximized`, `ResizeMode=NoResize`, `ShowInTaskbar=false`; `Closing` cancelled unless session submitted/force-submitted/unlocked | |
| `Services/Lockdown/LockdownCoordinator.cs` | Starts/stops all guards with the session; guarantees teardown in `finally` and `AppDomain.UnhandledException` | |

**Design — Backend**

- Extend `MonitoringEventType`: `KeyBlocked, ProcessKilled, UnauthorizedProcess, ClipboardBlocked, UsbInserted, MultiMonitor, RemoteSession, VirtualMachine, FocusLost` (keep `Violation` for compatibility). Migration: none (enum stored as int/string — verify column type).
- `Exam` gets `LockdownMode` (`Off | Report | Enforce`, default **`Report`** — Enforce terminates other programs without saving, so it must be a deliberate per-exam choice for lab PCs) and `BlockMultiMonitor` (bool, default true). Migration `AddLockdownConfig`.
- `GET /api/exams/student/current` returns `lockdownMode`, `allowedApps` so the client can configure guards.
- Violation severity mapping on server (`ViolationClassifier`): drives tile status Normal/Warning/Suspicious/Violation (thresholds configurable).

**Design — Web:** Create-exam step 1 gets "Lockdown mode" select + "Block multiple monitors". Live monitor shows violation type icons.

**Acceptance:** M4-U-01…13, M4-I-01…05, M4-E-01…04, M4-D-01…08, M4-M-01…06.
**Effort:** 8–10 dev-days (desktop) + 2 (backend/web).

**Status (branch `fyp1-security-hardening`):**
- ✅ Done: `LockdownPolicy`, `KeyboardHookService`, `ProcessGuardService`, `ClipboardGuardService`, `DeviceGuardService`, `LockdownCoordinator`; exam window fullscreen/topmost/uncloseable until submit; PID-based foreground check in `LiveMonitoringService`; `Exam.LockdownMode`/`BlockMultiMonitor` + migration `AddLockdownConfig`; `ViolationClassifier` (Warning → Suspicious → Violation); create-exam lockdown settings; live-monitor violation labels. Tests: M4-U-01…13, M4-I-01…05, M4-E-01…04 automated.
- ⏳ Remaining: `TaskManagerPolicy` (registry, optional), VM detection, FlaUI desktop tests M4-D-*, manual lab checks M4-M-*, code-signing / AV exclusion note.
- Client config fallback (`appsettings.desktop.json` / `SMARTEXAM_LOCKDOWN_MODE`): `LockdownMode` (default `Report`), `BlockMultiMonitor` — used only when the server sends no per-exam value.

### 3.2 Module 2 — Pre-Exam Verification & Mock Test (thesis FR2, `Ch-2-2`)

**Goal:** before the Start button enables, the workstation proves it can be proctored; students can rehearse with an ungraded mock exam.

**Design — Student_Desktop_App**

- `Services/Readiness/ReadinessCheckService.cs` runs checks, each returning `{name, passed, detail, durationMs}`:
  - **Network:** `GET /api/health` latency < 2 s (NFR), 3 attempts.
  - **Server clock skew:** compare `Date` header; |skew| < 60 s.
  - **Screen capture:** capture primary screen to memory (proves screenshot permission).
  - **Camera:** open default device and grab 1 frame (OpenCvSharp4 or WinRT `MediaCapture`) — required only if `Exam.RequireWebcam`.
  - **Microphone:** NAudio, record 1 s, RMS > threshold — only if `Exam.RequireMic`.
  - **Secure mode:** keyboard hook installs, single monitor (if required), not RDP, not VM.
  - **Disk:** ≥ 200 MB free for local backup (Module 6).
- `Views/ReadinessView` checklist UI with retry per item; result POSTed to server.
- Replace the fake "Verify identity" button (`DashboardViewModel.VerifyIdentityAsync` only logs an event) with: readiness pass + roll-number confirmation; face recognition stays **out of scope** per `Ch-1-4` (state this explicitly in the thesis).

**Design — Backend**

- New table `ReadinessChecks(Id, ExamId, UserId, PassedAll, ResultsJson, MachineName, CheckedAt)`. Migration `AddReadinessChecks`.
- `POST /api/exams/{id}/readiness` (Student) → stores result, audit `READINESS_PASSED/FAILED`.
- `Exam.RequireReadiness` (default true), `RequireWebcam`, `RequireMic`.
- `start-session` rejects with `READINESS_REQUIRED` unless a passing check exists for this exam within the last 30 min.
- **Mock exams:** `Exam.IsMock` flag. Mock exams: visible to every enrolled student any time, unlimited attempts (new session per attempt), excluded from results/analytics/plagiarism, labelled "Practice" in all UIs. Seeder adds one mock exam.
- `GET /api/exams/{id}/readiness` (staff) → per-student latest result; shown on Eligibility page and Live Monitor tile tooltip.

**Acceptance:** M2-U-01…06, M2-I-01…08, M2-E-01…04, M2-D-01…03, M2-M-01…05.
**Effort:** 5–6 dev-days.

**Status (2026-10-20)**
- ✅ Done — backend: `ReadinessCheck` table + migration `AddReadinessChecks` (also `Exam.RequireReadiness/RequireWebcam/RequireMic/IsMock`); `POST/GET /api/exams/{id}/readiness` (server recomputes `passedAll` and rejects a pass that skipped a required camera/mic); both start paths (`start-session` and the desktop's `student/start`) return `READINESS_REQUIRED` without a passing check ≤ 30 min old; `student/current` reports `requireReadiness`/`readinessPassed`/`serverTimeUtc`; `GET /api/exams/student/practice`; practice exams get a new session per attempt, are never picked as the dashboard's real exam, produce no plagiarism rows, and are excluded from student dashboard/trend and system analytics. `ReadinessUpdated` hub event.
- ✅ Done — desktop: `Core/Readiness/ReadinessPolicy` (pure rules) + `Services/Readiness/ReadinessCheckService` (health probe ×3, Date-header clock skew, GDI screen capture, WMI camera, `waveInGetNumDevs` mic, monitors/RDP/VM/keyboard-hook secure mode, ≥ 200 MB disk). Dashboard "Identity → Verify" replaced by "System check → Run check" with a per-item checklist and Retry; Start requires check-in + pass when the exam demands it; Practice picker + button. VM detection also reported live during the exam (`DeviceGuardService` → `VirtualMachine`).
- ✅ Done — web: create/edit wizard toggles (readiness on by default for new exams, webcam, mic, practice); Eligibility "System Check" column (✓ / ✗ / ⌛ expired / —) with failure tooltip; "Practice" badge on the student exams page.
- ⏳ Remaining: camera frame grab and mic level (currently device presence only — needs WinRT `MediaCapture`/NAudio), seeded practice exam on Neon, FlaUI tests M2-D-*, manual checks M2-M-*, readiness indicator on the Live Monitor tile.

### 3.3 Module 9 — Eligibility & Access Control (thesis FR9)

**Goal:** eligibility is computed from attendance with a logged manual override, and students can only sit the exam from their assigned seat.

**Design — Backend**

- New table `AttendanceRecords(SectionId, UserId, TotalClasses, AttendedClasses, UpdatedAt)`; `POST /api/sections/{id}/attendance/import` (CSV: `email,total,attended`).
- `Exam.MinAttendancePercent` (default 75). `ExamAssignment.EligibilityOverridden` (bool), `EligibilitySource` (`Attendance | Manual`).
- `POST /api/exams/{id}/eligibility/recalculate` → for non-overridden assignments sets `IsEligible = attended/total*100 ≥ min`, note `"Attendance 68% < 75%"`. Manual edits through the existing `PUT eligibility` set `EligibilityOverridden=true`. Every decision audited (`ELIGIBILITY_COMPUTED`, `ELIGIBILITY_OVERRIDDEN`).
- **Seat enforcement:** `Exam.EnforceSeat` (default true when a workstation is assigned). In `start-session` and every heartbeat: the student's `DeviceBinding.MachineName` must equal the assigned `Workstation.MachineNumber` **or** the request IP must equal `Workstation.IpAddress` (configurable `SeatMatch: MachineName | Ip | Either`). Mismatch → `403 SEAT_MISMATCH` + audit `SEAT_MISMATCH` + live-monitor alert.
- Fix: stop auto-registering every machine into "Systems Engineering Lab" (`AuthController.EnsureWorkstationRegisteredAsync`) unless `Labs:AutoRegister=true` — it defeats seat enforcement.

**Design — Web:** Eligibility page: attendance column, "Recalculate from attendance" button, override badge, CSV upload dialog.

**Acceptance:** M9-U-01…03, M9-I-01…10, M9-E-01…05.
**Effort:** 4 dev-days.

### 3.4 Module 3 — Exam Dashboard fixes

- Server: `start-session` rejects before `StartTime` (allow `Exam.EarlyJoinMinutes`, default 0) → `EXAM_NOT_STARTED`. Update `Module8ExamCreationTests` which currently starts a future exam.
- Client: Start button also requires `now ≥ StartTime` (server time from status call, not local clock) and readiness passed.
- Show readiness indicators (Module 2) on the dashboard.

**Acceptance:** M3-I-01…04, M3-E-01…03, M3-D-01…02. **Effort:** 1.5 dev-days.

---

## 4. Phase P2 — Monitoring & resilience

### 4.1 Module 5 — Live Monitoring Client Service (thesis FR5, `Ch-3-6`)

| Item | Design |
|------|--------|
| Screenshots | `ScreenshotService`: every `Exam.ScreenshotIntervalSeconds` (default 30, 0 = off) capture all screens → JPEG q=60, max 1280px → `POST /api/monitoring/screenshot` (multipart). Server stores under `Storage:EvidencePath/{examId}/{sessionId}/{ts}.jpg`, row in new `Screenshots` table. Also captured immediately on any violation. |
| Webcam snapshots | Optional (`Exam.WebcamIntervalSeconds`), same pipeline, `Kind=Webcam`. |
| Process telemetry | Full process list (name, PID, path hash) every heartbeat, diff-only after first; fixes "first 12 names" (`LiveMonitoringService.cs:123`). |
| Foreground app | From `ForegroundGuardService` (PID-based). |
| Batching | Events queued and flushed every 5 s (thesis `Ch-3-6`); heartbeat stays 10 s. |
| Offline buffer | `OfflineQueue` — append-only file, DPAPI-encrypted (`ProtectedData`, CurrentUser), each item has `ClientEventId` (GUID) + original timestamp; replayed in order on reconnect. Server dedups on `ClientEventId` (unique index). |
| Disconnect detection | Server marks session `Disconnected` when no heartbeat for 30 s (BackgroundService), broadcasts to live monitor; client logs local disconnect window and reports on reconnect. |
| Privacy/retention | `Storage:RetentionDays` (default 30) cleanup job; only staff of that exam can fetch screenshots. |

Backend: `MonitoringController` new endpoints `POST screenshot`, `POST events/batch`; `GET /api/exams/{id}/sessions/{sid}/screenshots` (staff, ownership-checked); migration `AddScreenshotsAndClientEventId`.
**Acceptance:** M5-U-01…06, M5-I-01…09, M5-E-01…03, M5-D-01…03. **Effort:** 7–8 dev-days.

### 4.2 Module 6 — Submission & Backup (thesis FR6)

| Item | Design |
|------|--------|
| Periodic auto-save | `ExamSessionViewModel`: dirty-flag + 10 s timer + save on question change + save on focus loss. |
| Encrypted local backup | Every change written to `%LOCALAPPDATA%\SmartExam\backup\{sessionId}.bin` (DPAPI + HMAC). On relaunch with an `InProgress` session → restore answers and continue. |
| Offline saves | Failed saves go to `OfflineQueue` (Module 5) instead of "will retry on submit" (`ExamSessionViewModel.cs:136`). |
| Idempotent save | `save-answer` accepts `clientSavedAt` + `revision`; server ignores stale revisions. |
| Submit receipt | Submit returns a receipt `{submittedAt, answerCount, sha256 of answers}`; client shows it and stores it; teacher results page shows the same hash. |
| Timer expiry | Client auto-submits at 0; server-side `TimedOut` status set by a BackgroundService for sessions still `InProgress` 2 min after deadline (grades via `SubmissionService.FinalizeAsync`). |
| Teacher force-submit | Already done (grades since `768df71`); add per-student button in UI (hub method exists). |

**Acceptance:** M6-U-01…05, M6-I-01…09, M6-E-01…04, M6-D-01…04. **Effort:** 5 dev-days.

### 4.3 Module 10 — Live Proctoring Dashboard (thesis FR10)

- Screenshot thumbnail per tile (latest), click → student detail drawer: event timeline, screenshots carousel, process list, readiness result.
- **Incidents:** new `Incidents(Id, SessionId, CreatedBy, Severity, Status Open|Escalated|Resolved, Notes, CreatedAt)`; mark/escalate/resolve from drawer; audit each change; included in reports.
- **Manual unlock:** hub `UnlockSession(sessionId, minutes, reason)` → client suspends lockdown for N minutes (e.g. invigilator fixes a PC), audited.
- **Per-student actions:** warn (exists), force-submit one student (hub exists, add UI), extend time per student (`ExamSession.ExtraMinutes`).
- **Invigilator role:** `/invigilator/live-monitor` route; new `ExamProctors(ExamId, UserId)`; invigilators see only assigned exams (privacy NFR); can warn/mark incidents, cannot grade.
- Disconnected state tile (Module 5).

**Acceptance:** M10-I-01…08, M10-E-01…10. **Effort:** 6 dev-days.

---

## 5. Phase P3 — Evaluation & evidence

### 5.1 Module 11 — AI-Assisted Evaluation (thesis FR11)

| Item | Design |
|------|--------|
| Code runner | New `Services/Grading/CodeRunner` behind interface `ICodeRunner`. `DockerCodeRunner`: per answer, container from `gcc:13` (C/C++) / `python:3.12-slim`, `--network none --memory 256m --cpus 0.5 --pids-limit 64 --read-only`, compile timeout 10 s, run timeout 2 s per test case. `FakeCodeRunner` for tests. |
| Async grading | `GradingQueue` (`Channel<Guid>`) + `GradingWorker` BackgroundService so submit returns immediately; status `Pending → Graded` on `AiGradingResult`. |
| Scoring | marks = Σ(weight of passed test cases) / Σ(weights) × question marks; justification lists each test case pass/fail with stdout diff (hidden cases shown to teacher only). Compile errors captured. |
| Theory grading | `ITheoryGrader`: default TF-IDF cosine vs teacher's model answer + rubric keywords (pure C#); optional `PythonSemanticGrader` calling a sentence-transformers microservice (`/grade`) when configured. Confidence from score margin. |
| Plagiarism | Replace Jaccard with token normalisation (strip comments/whitespace, rename identifiers → `ID`) + k-gram winnowing (MOSS-style). Pairwise matrix endpoint `GET /api/exams/{id}/plagiarism/matrix`; matched segments with line ranges for highlighting. |
| Teacher review | Existing override flow; add "Accept AI suggestion" bulk action; results page shows Pending/Graded/Overridden. |
| Remove heuristic | Keyword heuristic in `SubmissionService` becomes the fallback only when no runner/grader is configured, labelled as such (already honest since `768df71`). |

**Acceptance:** M11-U-01…10, M11-I-01…08, M11-E-01…04. **Effort:** 10–12 dev-days.

### 5.2 Module 12 — Logs, Analytics & Audit Trail (thesis FR12)

- **Tamper-evident audit:** `AuditLog.Sequence` (long), `PrevHash`, `Hash = SHA256(PrevHash ‖ Sequence ‖ ActorId ‖ EventType ‖ EntityId ‖ Details ‖ CreatedAt)`. Writes serialized through `AuditService` (single writer lock / DB sequence). `GET /api/audit-logs/verify` walks the chain and reports first broken link. Admin UI badge "Chain verified ✓".
- **Event coverage:** app switch, network disconnect/reconnect, eligibility decisions, AI grading, admin actions, readiness, incidents.
- **Reports (QuestPDF):** Attendance report, Violations report (with screenshot evidence), Scores report, existing Exam report. **Excel** (ClosedXML) for scores/attendance. **Evidence ZIP** per exam (screenshots + events CSV + audit extract + manifest with SHA-256 of each file).
- **Analytics:** plagiarism heat map (from matrix), violations-per-minute timeline, question difficulty index.

**Acceptance:** M12-U-01…04, M12-I-01…09, M12-E-01…05. **Effort:** 6 dev-days.

---

## 6. Phase P4 — Leftovers & polish

| Module | Task |
|--------|------|
| 1 | Real refresh tokens: `RefreshTokens(TokenHash, UserId, ExpiresAt, ReplacedBy, RevokedAt)`, rotation on use, reuse detection revokes the family; access token 30 min. Remove `"dummy-refresh-token"` (`AuthController.cs`). |
| 1 | HWID per thesis: BIOS serial + disk serial + baseboard serial (`DeviceFingerprint.cs`). Migration path: accept old hash once, then re-bind to new hash (audited). |
| 1 | Student login by email or unique `RollNumber` (add unique column) — not `Name`. |
| 7 | SuperAdmin pages (institution settings, admins); Invigilator UI (P2); Departments/Courses CRUD API + admin page. |
| 8 | Monitoring settings in create-exam (screenshot/webcam intervals, lockdown mode, readiness requirements); allowed-apps picker with common exe names. |
| — | Thesis: fill Chapters 4.3–4.5, 7 (paste test catalog + results), 8, 10; fix `Chapter-1-9.tex` template text; unify module numbering across README / status docs; retire `COMPLETION_CERTIFICATE.md`. |

**Acceptance:** M1-I-01…08, M7-I-01…05, M7-E-01…04, M8-E-01…03. **Effort:** 7 dev-days + docs.

---

## 7. Database migrations (in order)

1. `AddLockdownConfig` — Exam.LockdownMode, BlockMultiMonitor
2. `AddReadinessChecks` — ReadinessChecks table; Exam.RequireReadiness/RequireWebcam/RequireMic/IsMock/EarlyJoinMinutes
3. `AddAttendanceAndSeatEnforcement` — AttendanceRecords; Exam.MinAttendancePercent/EnforceSeat; ExamAssignment.EligibilityOverridden/EligibilitySource
4. `AddScreenshotsAndClientEventId` — Screenshots; MonitoringEvent.ClientEventId (unique); Exam.ScreenshotIntervalSeconds/WebcamIntervalSeconds; ExamSession.LastHeartbeatAt/ExtraMinutes
5. `AddAnswerRevision` — Answer.Revision, ClientSavedAt
6. `AddIncidentsAndProctors` — Incidents, ExamProctors
7. `AddGradingPipeline` — AiGradingResult.Status, TestCaseResultsJson; Question.ModelAnswer, RubricKeywords; TestCase.Weight
8. `AddAuditHashChain` — AuditLog.Sequence/PrevHash/Hash (backfill job computes chain for existing rows)
9. `AddRefreshTokensAndRollNumber`

Every migration: tested on a copy of the Neon DB first (`pg_dump` → local docker `Infra/docker-compose.postgres.yml`).

---

## 8. New / changed API surface (summary)

| Method & route | Role | Module |
|----------------|------|--------|
| `POST /api/exams/{id}/readiness` | Student | 2 |
| `GET /api/exams/{id}/readiness` | Staff | 2 |
| `POST /api/sections/{id}/attendance/import` | Teacher/Admin | 9 |
| `POST /api/exams/{id}/eligibility/recalculate` | Teacher | 9 |
| `POST /api/monitoring/screenshot` | Student | 5 |
| `POST /api/monitoring/events/batch` | Student | 5 |
| `GET /api/exams/{id}/sessions/{sid}/screenshots` | Staff (owner/proctor) | 5/10 |
| `POST/PATCH /api/incidents` | Teacher/Invigilator | 10 |
| hub `UnlockSession`, `ExtendStudentTime` | Staff | 10 |
| `GET /api/exams/{id}/plagiarism/matrix` | Teacher | 11 |
| `GET /api/audit-logs/verify` | Admin | 12 |
| `POST /api/analytics/reports/{type}` (`attendance|violations|scores|evidence-zip|excel`) | Staff | 12 |
| `POST /api/auth/refresh` (real) | Any | 1 |

Error codes are returned in the existing `ApiEnvelope` shape: `READINESS_REQUIRED`, `EXAM_NOT_STARTED`, `SEAT_MISMATCH`, `NOT_ELIGIBLE`, `EXAM_CLOSED`, `STALE_REVISION`.

---

## 9. Test strategy

| Layer | Tool | Location | Runs |
|-------|------|----------|------|
| Backend unit + API integration | xUnit + `WebApplicationFactory` + EF InMemory | `Backend_API.Tests` | `dotnet test`, CI |
| Desktop pure logic | xUnit | `Student_Desktop_App.Tests` (P0.8) | `dotnet test`, CI (windows runner) |
| Headless API (black-box over HTTP) | Playwright `request` | `Admin_Web_Panel/tests/api` | `npm run test:api`, CI |
| Headless browser E2E | Playwright (chromium, headless) + student-client simulator | `Admin_Web_Panel/tests/e2e` | `npm run test:e2e`, CI |
| Desktop UI automation | FlaUI (UIA3) | `Student_Desktop_App.UITests` | Manual/nightly on a Windows lab PC |
| Manual / lab | Checklists | `TEST_CASES.md` §M | Before each demo |

Tests for features not yet built are committed as **pending** (`test.fixme` in Playwright, `[Fact(Skip = "Pending: M…")]` in xUnit) with their IDs — they are the executable acceptance criteria. When a feature lands, un-skip its tests in the same PR.

Why Playwright rather than Selenium: it is already installed and configured headless in this repo, auto-waits (fewer flaky sleeps), and has a built-in HTTP client to simulate the WPF student client in the same test. Selenium would duplicate it with no extra coverage.

---

## 10. Risks

| Risk | Mitigation |
|------|------------|
| Keyboard hooks / process kill flagged by antivirus | Sign the exe (self-signed for lab), document AV exclusion; `Report` mode fallback |
| Ctrl+Alt+Del / power button can't be blocked | Detect via focus loss + heartbeat gap; that is the industry norm (SEB has the same limit) |
| DisableTaskMgr registry left on after crash | Marker file + restore on next start + uninstall script |
| Docker unavailable on lab server | `ICodeRunner` abstraction; fallback runner = honest heuristic, flagged in UI |
| Screenshot storage growth | JPEG compression, retention job, per-exam toggle |
| Neon latency for 30 concurrent clients | Batching (5 s), load test `M5-P-01` with k6 before demo |
| E2E flakiness from shared demo data | In-memory E2E backend reseeded per run; tests create their own exams |
