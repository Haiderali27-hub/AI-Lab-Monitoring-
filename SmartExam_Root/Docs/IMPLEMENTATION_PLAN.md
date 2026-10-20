# SmartExam — Implementation Plan (FYP-1 gaps → FYP-2 completion)

**Baseline:** branch `fyp1-security-hardening` (commit `768df71`), 105/105 backend tests passing.
**Scope source:** thesis `important docs/fyp_doc_3chatp/Chapters/Chapter-1-8.tex` (12 modules, 8 sprints), `Chapter-2-2.tex` (FR1–FR14, NFRs), `Chapter-3-6.tex` (architecture).
**Test IDs** referenced below are defined in [`TEST_CASES.md`](TEST_CASES.md).

---

## 0. Where we are

| # | Module | Thesis sprint | Status | Est. done |
|---|--------|---------------|--------|-----------|
| 1 | Authentication & Device Binding | S2 | **Complete** — rotating refresh tokens, roll number, HWID v2 (see §6 status) | 95% |
| 2 | Pre-Exam Verification & Mock Test | S4 | **Not started** | 0% |
| 3 | Student Exam Dashboard | S4 | **Start gate done** (see §3.4 status) | 95% |
| 4 | Secure Exam Environment (lockdown) | S4 | **Implemented incl. Task Manager policy, lab validation pending** (see §3.1 status) | 85% |
| 5 | Live Monitoring Client Service | S5 | **Implemented, lab validation pending** — screen/webcam captures, encrypted offline outbox + batching, disconnect watchdog, retention, timeline API, webcam tamper detection (see §4.1 status) | 85% |
| 6 | Submission & Backup | S6 | **Implemented, lab validation pending** — revisions, receipts, timeout sweeper, per-student extra time, encrypted local backup + offline queue + resume (see §4.2 status) | 85% |
| 7 | User & Role Management | S2 | **Complete** — SuperAdmin-only admin management, Invigilator UI + ExamProctors, Departments/Courses (see §6 status) | 95% |
| 8 | Exam Creation & Configuration | S3 | **Complete** — capture intervals + allowed-apps picker (see §6 status) | 95% |
| 9 | Eligibility & Access Control | S3 | **Implemented, lab validation pending** (see §3.3 status) | 90% |
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
- ✅ Done (Stage S2): Task Manager policy. Pure `Core/Lockdown/TaskManagerPolicy.cs` (`TaskManagerGuard` over `ITaskManagerPolicyStore` + `IRestoreMarkerStore`) sets `HKCU\…\Policies\System\DisableTaskMgr=1` while the exam window is up (Report + Enforce; config `DisableTaskManager`, default on, env `SMARTEXAM_DISABLE_TASKMGR`). The restore marker `%LOCALAPPDATA%\SmartExam\lockdown-restore.json` is written **before** the value; `LockdownCoordinator.Stop()` restores it on submit/close, unhandled exception and `ProcessExit`, and `App.OnStartup` recovers after a hard kill (skipped while another SmartExam instance still runs). `LockdownPolicy` escape-tool list (`taskmgr`, Process Explorer/Hacker/System Informer, `resmon`, `perfmon`, `regedit`, Autoruns) is caught even without a window: Enforce kills, Report reports. Windowless `cmd`/`powershell` stay allowed (IDE terminals). Tests M4-U-15…17 (fakes only — the real registry is never touched by tests). VM detection was already done (M4-U-14).
- ✅ Verifier fix (S2): `LockdownCoordinator.Stop()` runs its steps through `Core/Lockdown/LockdownTeardown` — each isolated, Task Manager restore first — because the crash / ProcessExit handlers run off the UI thread where the clipboard step can throw (D-15, M4-U-18).
- ⏳ Remaining: FlaUI desktop tests M4-D-* (M4-D-07 now has its unit half), manual lab checks M4-M-* (incl. M4-M-02 Task Manager under GPO), code-signing / AV exclusion note.
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

**Status (2026-10-20)**
- ✅ Done — data: migration `AddAttendanceSeatAndEarlyJoin` — `AttendanceRecords` (unique `SectionId+UserId`), `Exam.MinAttendancePercent` (existing rows 75) / `EnforceSeat` (**existing rows false**, new exams true) / `EarlyJoinMinutes`, `ExamAssignment.EligibilityOverridden` / `EligibilitySource` (string), `Workstation.HostName`; `MonitoringEventType.SeatMismatch` (severity Violation).
- ✅ Done — attendance: `POST /api/sections/{id}/attendance/import` (multipart `file`, ≤ 1 MB; columns found by header name in any order; unknown/inactive students and bad rows reported per line, non-enrolled students imported with a warning; upsert; teachers only for their own sections; audit `ATTENDANCE_IMPORTED`) and `GET /api/sections/{id}/attendance`. Pure `AttendanceCsvParser`.
- ✅ Done — eligibility: `POST /api/exams/{id}/eligibility/recalculate` (owner teacher/admin; 409 once Ended) via pure `EligibilityCalculator` (unrounded compare, note `Attendance 60% < 75%`; no record / zero classes → not computed, decision kept); `ELIGIBILITY_COMPUTED` per student + `ELIGIBILITY_RECALCULATED` summary. `PUT eligibility` turns only rows that actually change into `Manual` overrides (`ELIGIBILITY_OVERRIDDEN`) and accepts `clearOverride` (`ELIGIBILITY_OVERRIDE_CLEARED`). `GET assignments` adds `attendancePercent/attendedClasses/totalClasses/eligibilityOverridden/eligibilitySource/workstationHostName/workstationIp`.
- ✅ Done — seat enforcement (`Services/SeatGuard`): student-login stamps `client=desktop` + `machine=<PC name>` into the token; `start-session`, `student/start` and every heartbeat compare it (or the request IP — `SeatMatch: MachineName | Ip | Either`, top-level key) with the assigned workstation's `HostName ?? MachineNumber` / `IpAddress`, only when a seat is assigned and `EnforceSeat`. Start → 403 `SEAT_MISMATCH` + audit + `ViolationEvent` to `exam:{id}`, no session; heartbeat → `SeatMismatch` event (de-duplicated per seat for 5 min), audit, alert, `seatStatus/expectedSeat/actualMachine/seatDetail` on `StudentHeartbeat`. `student/current` reports `seatStatus` and folds it into `canStartNow`/message. Web-portal tokens carry no machine identity: they are checked by request IP in `Ip`/`Either` mode and not at all in `MachineName` mode; `Exams:RequireDesktopClientForStart=true` (recommended in production, see `appsettings.example.json`) refuses them with `DESKTOP_CLIENT_REQUIRED`.
- ✅ Done — labs: auto-registration only with `Labs:AutoRegister=true` (the `dotnet run` launch profiles set it for the demo; tests pin it off); `PUT/DELETE /api/labs/{labId}/workstations/{id}` (HostName, IP validation, 409 while assigned, audited) and `GET /api/labs/unregistered-machines`.
- ✅ Done — roster authorisation: `GET assignments`, `PUT eligibility`, `recalculate`, add/remove student and seat mapping are limited to the section's teacher (or an admin) — 404 for an unknown exam (M9-I-19, M9-A-04).
- ✅ Done — web: Eligibility page Attendance column, Manual/Attendance badge with "clear override", Upload Attendance dialog (CSV file → Import, per-line errors/warnings), Recalculate, minimum shown, dirty-rows-only save; Live Monitor "Seat mismatch: logged in from X (assigned Y)" + "Wrong seat" badge; Labs page PC-name field, edit/delete, unregistered-PC chips; create/edit wizard "Early join (minutes)", "Minimum attendance %", "Enforce assigned seat".
- ⏳ Remaining: manual lab check M9-M-01 (register each lab PC's Windows name); thesis note — the machine name is client-reported, HWID binds the student not the seat (future: admin-registered `Workstation.HwidHash`); FlaUI for the desktop seat message.

### 3.4 Module 3 — Exam Dashboard fixes

- Server: `start-session` rejects before `StartTime` (allow `Exam.EarlyJoinMinutes`, default 0) → `EXAM_NOT_STARTED`. Update `Module8ExamCreationTests` which currently starts a future exam.
- Client: Start button also requires `now ≥ StartTime` (server time from status call, not local clock) and readiness passed.
- Show readiness indicators (Module 2) on the dashboard.

**Acceptance:** M3-I-01…04, M3-E-01…03, M3-D-01…02. **Effort:** 1.5 dev-days.

**Status (2026-10-20)**
- ✅ Done — server: `start-session` and `student/start` refuse a start before `StartTime − EarlyJoinMinutes` with `EXAM_NOT_STARTED` (+ `startsAtUtc/opensAtUtc/serverTimeUtc`); `student/start` reports an over window as `EXAM_CLOSED` (`OUTSIDE_EXAM_WINDOW` is gone). `Exam.EarlyJoinMinutes` (0–60, validated; create/edit/detail). `student/current` adds `secondsUntilStart`, `opensAtUtc`, `earlyJoinMinutes`, `canStartNow`, `seatStatus`. `Module8ExamCreationTests` MCQ tests now start an exam that has begun.
- ✅ Done — desktop: pure `Core/Dashboard/StartGate` (clock offset from `serverTimeUtc`, so changing the PC clock does not unlock Start); dashboard clock reads "STARTS IN" until the opening, then "TIME REMAINING"; Start unlocks by itself at the opening (still needs check-in + readiness) without re-polling; server messages (`EXAM_NOT_STARTED`, `SEAT_MISMATCH`) are shown as-is.
- ✅ Done — web portal: countdown uses the exam's `startTime` (the fallback used "now", which ended the countdown at once).
- Fairness note: early joiners get up to `EarlyJoinMinutes` extra writing time — the end stays `StartTime + Duration`. Teachers should keep the window small (default 0).
- ⏳ Remaining: FlaUI M3-D-01/02.

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

**Status — Module 5 (Stage S5)**
- ✅ Server evidence: `POST /api/monitoring/screenshot` (alias `/screenshots`; Student, multipart `examSessionId, file, kind=Screen|Webcam, trigger=Interval|Violation|Manual, reason, capturedAt`). Own running session only (404 / 409 `SESSION_NOT_ACTIVE`, also after the deadline); JPEG/PNG decided by magic bytes (400 `INVALID_IMAGE`); > 10 MB → 413; webcam images only for `RequireWebcam` exams (400 `WEBCAM_NOT_REQUIRED`); ≤ `Monitoring:MaxScreenshotsPerMinute` (30) per session (429); absurd capture times → server time. Stored by `Services/Monitoring/EvidenceStorage` under `Storage:EvidencePath` (default `Evidence/`, git-ignored; tests and Playwright use temp folders) as `{examId}/{sessionId}/{capturedAt}_{id}.jpg` — server-generated names, root-contained paths, `.part` then move — with size + SHA-256 in the new `Screenshots` table; broadcast `ScreenshotUploaded` to `exam:{id}`.
- ✅ Staff (`MonitoringEvidenceController`, Teacher/Admin/SuperAdmin/Invigilator + `ExamAccessService`: owner, assigned proctor or admin; students 403 even for their own session): `GET /api/exams/{id}/sessions/{sid}/screenshots` (newest first, `contentUrl`), `GET /api/screenshots/{id}/content` (`Cache-Control: private, max-age=300`, `nosniff`; the web page fetches it as a blob), `GET /api/exams/{id}/sessions/{sid}/timeline?after=&limit=` (events without heartbeats, captures, proctor warnings, Started/Submitted — newest first, with `severity` and the client/received times; `after` compares the server receive time, so the next cursor is the largest `receivedAt ?? at`).
- ✅ `POST /api/monitoring/events/batch` (≤ 200 events, all-or-nothing validation, payload ≤ 8 KB valid JSON): dedup on `ClientEventId` in code + partial unique index `(ExamSessionId, ClientEventId)`; client `recordedAt` kept unless > 2 min ahead / > 24 h old (then server time + `clockAdjusted`); `ReceivedAt` stored; live `ViolationEvent` only for violations < 60 s old (no alert storm after an outage). Server-only types (`SeatMismatch`, `Disconnected`, `Reconnected`, `Heartbeat`) cannot be posted by a client (D-25).
- ✅ Liveness: heartbeat stamps `ExamSession.LastHeartbeatAt`; `HeartbeatWatchdogService` (`Monitoring:DisconnectAfterSeconds` 30, `DisconnectSweepSeconds` 5) flags silent running sessions once (`IsDisconnected`, `Disconnected` event, `StudentDisconnected`); the next heartbeat records `Reconnected {offlineSeconds}` and sends `StudentReconnected` before `StudentHeartbeat`. Heartbeat rows are stored only on change or once a minute (liveness is the column); the broadcast adds `processes`, `idleSeconds`, `cameraStatus`, `lastHeartbeatAt`, `isDisconnected`; the ack adds `screenshotIntervalSeconds`, `webcamIntervalSeconds`, `requireWebcam`. A heartbeat racing a finalisation reloads and retries its liveness update.
- ✅ `EvidenceRetentionService` (`Storage:RetentionDays` 30, `CleanupIntervalSeconds` 3600): file first, then row; rows whose file is already gone are removed; old files no row refers to are deleted too. Migration `AddMonitoringEvidence` (additive: `Screenshots`, `MonitoringEvents.ClientEventId/ReceivedAt` + indexes, `ExamSessions.LastHeartbeatAt/IsDisconnected/DisconnectedAt`). New event types `Disconnected`, `Reconnected`, `NetworkOffline` (not violations), `CameraCovered`, `CameraFrozen` (Suspicious), `CameraUnavailable` (Warning).
- ✅ Desktop (pure, unit-tested in `Core/Monitoring`): `MonitoringEventQueue` (append-only outbox, each record AES-256-GCM sealed with the session id as associated data, key DPAPI-wrapped in `key.bin` — `SessionKeyFile`/`RecordCipher`; tampered records skipped, torn tail dropped), `CaptureSpool` (encrypted pending captures, bounded, interval captures dropped before violation evidence), `MonitoringSpool` (`%LOCALAPPDATA%\SmartExam\monitoring\{sessionId}`, reset + reported if its key no longer opens, 7-day cleanup at app start), `EventBatcher` (5 s, violations at once, 5→60 s backoff offline, replay on the next good heartbeat), `ProcessDiff`, `CaptureSchedule` (random 0.5–1.5× the exam's mean → 30–90 s by default, violation capture ≤ 1/15 s, longest side ≤ 1280 px), `JpegEncoder` (WPF imaging, q60), `IdleTime`, `FrameMetrics` + `CameraTamperDetector` (covered = mean < 25 or Laplacian variance < 15 for 3 s; frozen = frame difference < 0.3 for 15 s on an uncovered picture; unavailable = no frame for 5 s; cleared after 2 s; one event per episode), `Delivery`.
- ✅ Desktop services: `ScreenCapture` (GDI BitBlt of the whole virtual screen, all monitors; also the readiness "Screen capture" check), `EvidenceCaptureService` (interval + violation captures off the UI/hook threads, spooled, uploaded oldest first; 409/400/413 dropped, offline kept), `WebcamCapture` + `WebcamMonitorService` (OpenCvSharp4 + runtime.win, DirectShow, ≈ 1.5 FPS on a background thread, only when `RequireWebcam`; webcam snapshots on the exam's webcam interval and on each raised issue), `LiveMonitoringService` (heartbeat with `GetLastInputInfo` idle seconds and camera status, process changes as `ProcessList` events, focus loss / lockdown violations via `ReportViolation` → outbox + screen capture, `NetworkOffline {from,to}` after an outage, capture settings followed from the ack, final drain on stop). `LockdownCoordinator` reports into the outbox (D-26). `ApiClient.SendEventBatchAsync` / `UploadCaptureAsync`.
- ✅ Module 2: the readiness Camera check (when `RequireWebcam`) now opens the camera and grabs a real frame (8 s timeout): no picture or a dark/featureless picture fails (`ReadinessPolicy.EvaluateCamera`, M2-U-09).
- ✅ Web: Live Monitor tiles show the latest screen capture (`tile-thumbnail`, blob URL fetched with the token, revoked on replace/unmount), "Disconnected" (dashed grey) from `StudentDisconnected` until a heartbeat or `StudentReconnected`, "Idle Nm" and camera badges; camera violation labels; `api/monitoring.api.ts` (screenshots, blob, timeline) ready for the M10 drawer.
- S5 verifier fixes: the webcam loop can no longer end the exam app (any camera/OpenCV/subscriber failure is absorbed; `IWebcamFrameSource` seam, D-27); timeline `?after=` filters on the server receive time so replayed events reach an incremental refresh (D-28); a reconnecting heartbeat that races a finalisation keeps its reconnect (D-29).
- Tests: M5-U-01…17, M5-I-01…20, M5-A-03…05, M5-E-02…04, M2-U-09 (+ classifier cases). Manual/FlaUI on a lab PC only: M5-D-01…03 (`Student_Desktop_App` must not be launched on the dev PC), M5-P-01 (k6).
- Not in this stage: the per-exam timeline grid and `GET /api/exams/{id}/live` snapshot, drawer screenshot carousel / process list / incident UI (Module 10, M10-E-06/08); face presence / multiple faces / head pose (YuNet) and phone detection (ONNX) — webcam monitoring here is tamper detection only; D-06 (foreign-session heartbeat still answers 200, not attributed). Known narrow race: a watchdog sweep that loaded a session just before its heartbeat can flag it Disconnected for one beat (the next heartbeat, ≤ 10 s later, clears it with a `Reconnected` event).

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

**Status — Module 6 (Stage S4)**
- ✅ Per-student deadline: `ExamSession.ExtraMinutes`; `SubmissionService.EffectiveEnd(session)` (exam end + extra minutes) and `IsClosed(session, now)` are used by save-answer, the heartbeat `remainingSeconds`, `student/current` (`remainingSeconds`, `examEndUtc`) and both start paths (a running session can be **resumed** inside the student's own time; nobody can start a new one after the exam window). Ending the exam (force-submit-all) still closes everyone, extra time included. Hub `ExtendStudentTime(sessionId, minutes)` (1–180 per grant, ≤ 600 in total; anyone supervising the exam — owner teacher, assigned invigilator, admin; the whole exam stays owner/admin-only), audited `SESSION_TIME_EXTENDED`, pushed as `StudentTimeExtended` to `exam:{id}` and to the student.
- ✅ Revisions: `Answer.Revision` (concurrency token) + `ClientSavedAt`; save-answer `{questionId, answerText, revision?, clientSavedAt?}` → `{message, lastSavedAt, revision}`. Lower revision, or the same revision with different text → **409 `{code: STALE_REVISION, currentRevision}`**; same revision + same text → 200 no-op (offline replay); no revision → last write wins as before. A session the sweeper timed out answers "Exam time is over…". New checks: the question must belong to the session's exam (D-21), text ≤ 100 000 chars. Unique `Answers(SessionId, QuestionId)` (also enforced in code + retry for the InMemory provider).
- ✅ Receipt: `Services/ReceiptHasher` (canonical `SMARTEXAM-RECEIPT-v1`, SHA-256 hex) fixed in `FinalizeAsync` → `ExamSession.ReceiptSha256/ReceiptAnswerCount`. Submit returns flat `{message, sessionId, submittedAt, answerCount, sha256}`; teacher results and the student result endpoint return `receiptSha256`/`receiptAnswerCount`. `FinalizeAsync` broadcasts `SessionFinalized` (all paths: submit, force-submit, force-submit-all, timeout); `ExamSession.Status` is a concurrency token so two finalisations cannot both win (the loser gets 400 / HubException / is skipped).
- ✅ `Services/SessionTimeoutService` (BackgroundService, `Submission:TimeoutSweepSeconds`, default 30, 0 = off; the xUnit factory turns it off except in the sweeper tests): times out sessions past `EffectiveEnd + 2 min` (or of an Ended exam) through `FinalizeAsync(TimedOut)` — graded, receipt — audited `SESSION_TIMED_OUT`; one scope per session, concurrent submits are skipped.
- ✅ `GET /api/exams/sessions/{id}/answers` (student, own session; 404 otherwise): `{sessionId, status, remainingSeconds, extraMinutes, receipt, answers:[{questionId, answerText, revision, lastSavedAt}]}`. Heartbeat ack adds `sessionStatus` and `extraMinutes`. Hub `ForceSubmitSessionById(sessionId)` (D-20) audited `SESSION_FORCE_SUBMITTED` (also the old `ForceSubmitSession`). New event type `BackupTampered` (Suspicious).
- ✅ Desktop (pure, unit-tested): `Core/Submission/AutosaveScheduler` (10 s after the first unsaved edit), `OfflineSaveQueue` (in-order replay, newest revision per answer, response classification), `AnswerMerge` (server vs backup, higher revision wins), `ReceiptHasher` (same vectors as the server); `Core/Backup/AnswerBackupCodec` (AES-256-GCM, session id as associated data) + `AnswerBackupStore` (`%LOCALAPPDATA%\SmartExam\backup\{sessionId}.seb`, per-session key wrapped by DPAPI CurrentUser with the session id as entropy — `Services/Backup/DpapiKeyProtector`; atomic `.tmp` + replace; 7-day / 30-day cleanup at app start). A DPAPI key is tied to the Windows profile: a backup cannot be restored on another PC (by design; the server copy is authoritative).
- ✅ Desktop wiring: every edit bumps the answer's revision; the exam window saves on question change and on focus loss, autosaves every 10 s, writes the backup within ~1 s, shows "Offline — saved on this PC, will sync" and replays when the network returns. On (re)open it merges the server answers with the backup and replays unsent ones; a tampered backup is ignored and reported. Submit flushes everything first and **never closes while answers exist only on this PC** (D-19; auto-submit retries every 10 s); success shows the receipt with "Verified" / "Mismatch" and marks the backup submitted. The exam window resyncs its countdown from every heartbeat (D-16) and closes when the heartbeat reports a force-submit / timeout (D-17). Dashboard: "Resume Exam" for an `InProgress` session without a new check-in (`StartGate.CanResume`, D-18) and a notice when unsent answers are saved on this PC.
- ✅ Web: results page "Receipt" column (`data-testid="receipt-hash"`, full hash in the DOM); student result page receipt; Live Monitor tiles show Submitted / Force submitted / Timed out from `SessionFinalized` (M6-E-03), force-submit by session id, per-student "Extra Time for Student" in the drawer (`+N min` on the tile).
- ✅ Verifier fixes (S4): timed-out sessions take part in plagiarism detection (D-23); `student/current` and `student/start` prefer the session the student is still running in extra time over the next scheduled exam (D-24).
- Tests: M6-U-01…09, M6-I-01…03, M6-I-07, M6-I-10…20, M10-I-05, M6-A-05…08, M6-E-03…06. Manual/FlaUI on a lab PC only: M6-D-01…04.
- Not in this stage: replaying offline answers after the deadline (`Submission:OfflineReplayGraceMinutes`, design §4.6 — rejected today, kept in the encrypted backup); a SignalR client in the desktop app (warnings/extra time still arrive with the next heartbeat, ≤ 10 s). Known narrow races left as they are: an autosave committed in the same instant as a force-submit/timeout can be stored but miss the receipt and grading (the student's own submit flushes first, so it is not affected); two proctors granting the same student extra time in the same instant can lose one grant (last write wins).

### 4.3 Module 10 — Live Proctoring Dashboard (thesis FR10)

- Screenshot thumbnail per tile (latest), click → student detail drawer: event timeline, screenshots carousel, process list, readiness result.
- **Incidents:** new `Incidents(Id, SessionId, CreatedBy, Severity, Status Open|Escalated|Resolved, Notes, CreatedAt)`; mark/escalate/resolve from drawer; audit each change; included in reports.
- **Manual unlock:** hub `UnlockSession(sessionId, minutes, reason)` → client suspends lockdown for N minutes (e.g. invigilator fixes a PC), audited.
- **Per-student actions:** warn (exists), force-submit one student (hub exists, add UI), extend time per student (`ExamSession.ExtraMinutes`).
- **Invigilator role:** `/invigilator/live-monitor` route; new `ExamProctors(ExamId, UserId)`; invigilators see only assigned exams (privacy NFR); can warn/mark incidents, cannot grade.
- Disconnected state tile (Module 5).

**Acceptance:** M10-I-01…08, M10-E-01…10. **Effort:** 6 dev-days.

**Status — Module 10 (Stage S6)**
- ✅ Incidents: `Incidents(IncidentId, SessionId, ExamId, CreatedBy, Severity Low|Medium|High|Critical, Status Open|Escalated|Resolved, Notes ≤ 2000, StatusNote, CreatedAt, UpdatedAt, ResolvedAt, ResolvedBy)` (migration `AddIncidentsAndUnlock`; rows go with their session). `IncidentsController` (Teacher/Admin/SuperAdmin/Invigilator + `ExamAccessService`; students 403 by role): `POST /api/incidents {sessionId, severity, notes}` → 201 (unknown and foreign sessions both 403; `INVALID_SEVERITY`, `NOTES_REQUIRED`, `NOTES_TOO_LONG`), `PATCH /api/incidents/{id} {status?, notes?}` (Open→Escalated, Open→Resolved, Escalated→Resolved; the note of a status change is kept as `statusNote`; without a status change the notes replace the description; Resolved is final → 400 `INVALID_TRANSITION`; unknown status → 400 `INVALID_STATUS`), `GET /api/exams/{id}/incidents?status=&sessionId=` (newest first, student and staff names). Audit `INCIDENT_CREATED` / `INCIDENT_UPDATED` / `INCIDENT_ESCALATED` / `INCIDENT_RESOLVED` (only on success); `IncidentChanged` to `exam:{id}`.
- ✅ Manual unlock: hub `UnlockSession(sessionId, minutes 1–30, reason)` (reason required, ≤ 500) sets `ExamSession.UnlockedUntil`, audit `SESSION_UNLOCKED {minutes, reason, until}`, `SessionUnlocked` to the student and the exam group; `RelockSession(sessionId)` ends it early (`SESSION_RELOCKED`, `SessionRelocked`). Only staff who supervise the exam, only running sessions. The heartbeat ack carries `unlockedUntilUtc`; the desktop `LockdownSuspendPolicy` converts it to the PC's clock with the ack's server time (capped at 30 min) and decides Suspend / Reschedule / Resume; `LockdownCoordinator.Suspend()/Resume()` turn off and back on the Task Manager policy, keyboard hook, clipboard guard and process guard (device guard keeps reporting; the window still refuses to close); the window drops Topmost, shows a blue notice, re-locks on its own timer at the deadline and reports `LockdownSuspended` / `LockdownResumed` (new non-violation event types). Focus loss is not reported while unlocked.
- ✅ D-05 warning push (design §1.2, heartbeat-ack command channel — the WPF app has no SignalR client): `SendWarning` validates the text (≤ 500), stores the notification with `RelatedEntityId = sessionId`, audits `PROCTOR_WARNING_SENT`, pushes `ProctorWarning` to `Clients.User(studentId)` and `ProctorWarningSent` to the exam group. The heartbeat ack returns up to 5 unread warnings of the running session (`warnings[] {notificationId, title, body, createdAt}`) until the student acknowledges them; `PATCH /api/notifications/{id}/read` of a warning is audited `PROCTOR_WARNING_ACKNOWLEDGED`. Desktop: `Core/Monitoring/WarningInbox` (each warning once, oldest first, remembers acknowledgements made offline), `LiveMonitoringService.WarningsReceived`, an amber non-modal banner on the exam window with Acknowledge. Latency ≤ one heartbeat (10 s).
- ✅ Board snapshot `GET /api/exams/{id}/live` (staff with access): every student who has heartbeated (latest session each) with status, liveness, extra time, unlock, answered/total, violation count + server tile status + last violation, the latest stored heartbeat (window, apps, idle, camera), latest screen capture and open incidents — a Live Monitor opened or reloaded mid-exam starts complete (D-30). `GET /api/exams/{id}/sessions/{sid}/processes`: apps running now and every app seen during the session (first/last sighting). The session timeline now also lists incidents and proctor actions (unlock, relock, extra time, force-submit) and a violation capture's reason. `StudentHeartbeat` carries `extraMinutes` and `unlockedUntilUtc`.
- ✅ Web (`LiveMonitorPage` + `pages/teacher/live/`): snapshot on load / exam switch (live events win), tile badges `tile-readiness` (Ready / Check failed / No check, refreshed on `ReadinessUpdated`), `incident-badge`, `tile-unlocked`; student drawer (`StudentDrawer`): capture strip (`screenshot-carousel`, blob URLs revoked, click to enlarge), readiness items, apps (`process-list`, closed apps struck through), incidents with Escalate / Mark resolved + note, server-backed `activity-timeline` (re-read after each live event about the student and every 15 s), actions: warning (+ "Warning Message Sent"), Mark incident (Description + Severity), Unlock 5 min with a recorded reason / Lock again, extra time, force-submit. New actions report through the toast, not `alert()`.
- Tests: M10-I-01…04 un-skipped; added M10-U-01…07 (desktop), M10-I-09…14, M10-A-01…03, M10-E-13…14; M10-E-06…09 un-fixme'd (M10-E-08 now uploads the capture it checks for — the pending test never did). Lab-PC only: M10-D-01…02.
- Not in this stage: the per-exam timeline grid (rows = students, 1-minute buckets) and `GET /api/exams/{id}/timeline?bucketSeconds=`; a SignalR client in the desktop app (warnings and unlocks arrive with the next heartbeat, ≤ 10 s); incidents in the exported reports (Module 12); ownership checks on `extend-time` / `force-submit-all` (see Module 7 notes).

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

**Status — Module 1 (Stage S2, 2026-10-20)**
- ✅ Refresh tokens (`Models/RefreshToken.cs`, `Services/RefreshTokenService.cs`): 64-byte random token (Base64Url), only the SHA-256 hex hash stored (unique index); a family per login with an absolute expiry (`Jwt:StudentRefreshHours` / `Jwt:StaffRefreshHours`, default 12 h — rotation never extends it). `POST /api/auth/refresh` (anonymous, own rate-limit policy `refresh`) rotates: old token `rotated` + `ReplacedByTokenId`, new access token (+ `UserSession`) re-emitting the desktop `client`/`machine` seat claims, successor in the same family. Replaying a rotated token (or racing it — `RevokedAt` is a concurrency token) revokes the whole family **and** every access token it issued, audits `REFRESH_TOKEN_REUSE`, returns 401 `REFRESH_TOKEN_REUSED`. Logout revokes the caller's family; force-logout and deactivate revoke all of the user's tokens (`FORCE_LOGOUT` audit carries `revokedRefreshTokens`). Staff `login` returns `refreshToken` + `accessTokenExpiresAtUtc` (appended fields).
- ✅ Access tokens: `iat` claim, single clock reading (`exp − iat` exactly the lifetime); code default 30 min (`appsettings.example.json` 30; the xUnit factory still pins 480; Playwright's backend runs with 30). **Set `Jwt:ExpiryMinutes` to 30 in your local/deployed `appsettings.json`** — it is untracked and still says 480.
- ✅ Desktop: pure `Core/Auth/TokenRefreshGate` (single-flight, proactive refresh 60 s before expiry, transient errors keep the session); `ApiClient.SendAuthorizedAsync` reads the current token at send time and refreshes + retries once on 401 for **every** authorized call (save-answer, submit, start-session, exam detail, readiness, notifications, logout included), so windows/services holding an old token string are safe. `IsSessionAliveAsync` and the heartbeat only kick out after a refresh is refused.
- ✅ Web: `src/api/client.ts` single-flight refresh (Web Locks across tabs), proactive refresh in the request interceptor, retry-once on 401, login redirect only when the refresh is refused; `getFreshAccessToken()` for SignalR `accessTokenFactory` (Live Monitor reconnects after expiry) and the PDF `fetch` downloads; logout now calls `POST /api/auth/logout` (it used to clear localStorage only — defect D-12).
- ✅ Roll number: `User.RollNumber` (≤ 32, trimmed + upper-case, filtered unique index); create / update (`null` = unchanged, `""` = clear, duplicate → 409) / CSV import (`rollNumber` column) / list + detail; Users page field, column and search. Student login = email **or** roll number; the non-unique display name no longer logs in. Demo seed: Ali `BSCS-21-001`, Sara `BSCS-21-002`. WPF prompt "Email or roll number".
- ✅ HWID v2: desktop `DeviceFingerprint.CreateRawFingerprintV2()` = `v2|BIOS|system disk (Index 0)|baseboard` with OEM-junk filtering and an SMBIOS-UUID / MachineGuid fallback when fewer than two serials are usable; the legacy v1 string is sent once as `legacyHardwareFingerprint`. Server: `DeviceBinding.HwidVersion` (existing rows = 1); a v1 binding is re-bound to the v2 hash only when the same login proves the old fingerprint (`DEVICE_REBOUND` audit), after which only v2 is accepted. New bindings from a v2 client start at version 2.
- Tests: M1-I-01…07 un-skipped; M1-I-09…12, M1-A-07…10, M1-E-03…06, M1-U-01…03 added.
- ✅ Verifier fixes (S2): logout also revokes the access tokens the family issued earlier; a password change revokes every other session (refresh families + access tokens, audit `PASSWORD_CHANGED` with counts), an admin password reset all of the user's sessions; the web logout refreshes proactively so an idle-expired token still revokes the family (D-13); `reset-demo-data` clears `RefreshTokens`. Tests M1-I-13, M1-E-07.
- Known limitation: if the refresh **response** is lost on the network after the server rotated, the client's retry is a replay and ends the session (the student logs in again and resumes the in-progress session). No grace window, by design of M1-I-03.

**Status — Module 7 (Stage S3)**
- ✅ `ExamProctors(ExamId, UserId, AssignedBy, AssignedAt)` (PK both ids, cascades with exam/user; `AssignedBy` is a plain id like an audit field) — built **once** here; M10 reuses it. One contract satisfies M7-I-05 and M10-I-08: `POST /api/exams/{id}/proctors {userId}` (section teacher or Admin/SuperAdmin) → **201** new, **200** repeat (idempotent; a concurrent duplicate is caught on the composite key); target must be an active **Invigilator or Teacher** (`INVALID_PROCTOR_ROLE`, `USER_INACTIVE`, the section's own teacher → `ALREADY_OWNER`); `GET` lists, `DELETE …/proctors/{userId}` → 204 / 404. Audit `PROCTOR_ASSIGNED` / `PROCTOR_UNASSIGNED`.
- ✅ `Services/ExamAccessService` (`None < Proctor < Owner < Admin`) is the one authority for "may supervise exam X". `GET /api/exams` returns an invigilator only proctored exams; `GET /api/exams/{id}` is 404 for an unassigned invigilator and hides the answer key (reference answer, MCQ key, hidden test cases) from proctors; `GET /api/exams/{id}/readiness` is 403 without access (also for other sections' teachers). Hub: `JoinExamGroup` needs access (admins any exam, a teacher their sections', anyone else an `ExamProctors` row; unknown exam → `HubException`); `ForceSubmitSession` and `SendWarning` (D-05 ownership part) need access to the student's in-progress exam. Teachers keep seeing every exam in `GET /api/exams` (unchanged).
- ✅ SuperAdmin guard (`UsersController`): creating an Admin/SuperAdmin, or editing / promoting / demoting / deleting / (de)activating / force-logging-out / resetting the binding of one, needs a SuperAdmin → 403 `SUPERADMIN_REQUIRED` + audit `PRIVILEGED_ACCOUNT_DENIED`; CSV rows with an admin role are skipped and reported; undefined role values → 400. Acting on one's own account keeps the existing self-rules (no own role change/delete/deactivate). First SuperAdmin: demo seed `root@smartexam.com / Super@123`; an existing database (Neon) is bootstrapped from `Bootstrap__SuperAdminEmail` (+ `Bootstrap__SuperAdminPassword` when the account must be created) at startup — promotes or creates, audited `SUPERADMIN_BOOTSTRAPPED`, no-op once any SuperAdmin exists (`Data/SuperAdminBootstrap.cs`).
- ✅ Departments/Courses API (`DepartmentsController`, `CoursesController`): GET for Teacher/Admin/SuperAdmin/Invigilator, writes Admin/SuperAdmin. Names unique case-insensitively (≤ 128), course codes trimmed + upper-cased, unique per department (≤ 32); dept with courses / course with sections → 409 (`DEPARTMENT_HAS_COURSES`, `COURSE_HAS_SECTIONS`); audited `DEPARTMENT_*` / `COURSE_*`.
- ✅ **Bug fixed:** `Course → Department` was bound to an EF shadow column `DepartmentDeptId`; the mapped `Course.DeptId` stayed all-zero for seeded rows. Now `HasForeignKey(c => c.DeptId)` (Restrict). Migration `AddAcademicsAndExamProctors` copies `DepartmentDeptId` into `DeptId` before dropping it, then adds the unique indexes (`Departments(Name)`, `Courses(DeptId, Code)`), `ExamProctors` and the FK. Rehearsed on a throwaway local Postgres 16 container (script from `dotnet ef migrations script`): a seeded-style course keeps its department. **Before applying to Neon**, run the duplicate checks in the migration's header comment.
- ✅ Web: role-aware routing (`utils/roles.ts`: `homePathFor`, `assignableRoles`, `canManageAccount`); Invigilator → `/invigilator/live-monitor` (login and `/login` redirect, cached-session role check accepts Invigilator). Live Monitor is shared: invigilators get a proctor badge, only the Live Monitor nav item, no extend / force-end / create-exam controls; all roles get an exam picker (`data-testid="exam-picker"`, `?examId=`) over the server-scoped running exams, and switching clears the board. Users page offers Admin/SuperAdmin roles only to a SuperAdmin and locks admin rows for an Admin; Admin tab includes SuperAdmins. New `/admin/academics` page (Departments & Courses) in the admin sidebar. Eligibility page → "Invigilators" panel (`components/exam/ExamProctorsPanel.tsx`) to assign/remove proctors.
- Tests: M7-I-02…05 and M10-I-08 un-skipped; added M7-I-06…12, M7-U-01…02, M7-A-05…07, M7-E-05…08; M7-E-04 and M10-E-10 un-fixme'd; M10-E-11 added.
- ✅ Verifier fixes (S3): withdrawn access now also applies to pages already open — `Services/ExamGroupTracker` (singleton) records which hub connection joined which `exam:{id}` group; unassigning a proctor, deactivating / deleting / force-logging-out an account or changing its role removes those connections from the group at once and sends them `ExamAccessRevoked` (Live Monitor shows a banner and clears the board). `JoinExamGroup` re-checks access after joining to close the race with a concurrent unassign. A role change now ends every session of that account (`role_changed`), so a demoted Admin's access token stops working immediately. `GET /api/exams` (staff) returns `canSupervise`, and the Live Monitor opens a supervisable exam by default instead of another section's. Department/course unique-index races return 409 instead of 500. Tests: M7-I-13…15, M7-U-03, M7-A-08, M7-E-09, M10-E-12.
- Not in this stage: SuperAdmin "institution settings" page; Sections CRUD (sections still come from the seed/DB); ownership checks on `extend-time`, `force-submit-all`, `DELETE /api/exams/{id}` and teacher-wide `GET /api/exams/{id}` (any teacher can still call them for any exam — M10 hardening).

**Status — Module 8 (Stage S2)**
- ✅ `Exam.ScreenshotIntervalSeconds` / `WebcamIntervalSeconds` (default 60, validated 10–600 on create/edit; migration `AddCaptureIntervals`), returned by exam detail and `/student/current` (desktop `StudentExamStatus` has the fields for the Module 5 capture service). The value is the **mean**: the client randomises each wait ±50%, so the default gives the thesis' random 30–90 s schedule, plus an immediate capture on a violation.
- ✅ Wizard step 3: "Screenshot interval" / "Webcam interval" inputs (prefilled on edit, validated in the wizard); allowed-apps picker (`components/exam/AllowedAppsPicker.tsx`, catalogue in `utils/allowedApps.ts`: VS Code, Code::Blocks, Dev-C++, Python IDLE, Eclipse, IntelliJ, PyCharm, NetBeans, Visual Studio, Notepad++, …) with chips and a free-text fallback normalised to `name.exe`. Early join / min attendance / enforce seat were already in place (Stage S1).
- Tests: M8-I-01, M8-E-02, M8-E-03.

---

## 7. Database migrations (in order)

1. `AddLockdownConfig` — Exam.LockdownMode, BlockMultiMonitor
2. `AddReadinessChecks` — ReadinessChecks table; Exam.RequireReadiness/RequireWebcam/RequireMic/IsMock/EarlyJoinMinutes
3. ✅ `AddAttendanceSeatAndEarlyJoin` — AttendanceRecords; Exam.MinAttendancePercent/EnforceSeat/EarlyJoinMinutes (EarlyJoinMinutes was not in #2 after all); ExamAssignment.EligibilityOverridden/EligibilitySource; Workstation.HostName
4. `AddScreenshotsAndClientEventId` — Screenshots; MonitoringEvent.ClientEventId (unique); ExamSession.LastHeartbeatAt/ExtraMinutes (Exam.ScreenshotIntervalSeconds/WebcamIntervalSeconds already added by ✅ `AddCaptureIntervals`, Stage S2)
5. ✅ `AddSubmissionReceiptAndRevision` (Stage S4) — Answer.Revision, ClientSavedAt; unique Answers(SessionId, QuestionId) (replaces the SessionId index — check for duplicates first, query in the migration header); ExamSession.ExtraMinutes (moved here from #4), ReceiptSha256 (64), ReceiptAnswerCount
6. `AddIncidents` — Incidents (ExamProctors already added by ✅ `AddAcademicsAndExamProctors`, Stage S3)
7. `AddGradingPipeline` — AiGradingResult.Status, TestCaseResultsJson; Question.ModelAnswer, RubricKeywords; TestCase.Weight
8. `AddAuditHashChain` — AuditLog.Sequence/PrevHash/Hash (backfill job computes chain for existing rows)
9. ✅ `AddRefreshTokensRollNumberHwidV2` — RefreshTokens (hash unique, family/jti/user indexes, FK cascade); User.RollNumber (filtered unique); DeviceBinding.HwidVersion (default 1 for existing rows). All additive.
10. ✅ `AddCaptureIntervals` — Exam.ScreenshotIntervalSeconds/WebcamIntervalSeconds (default 60).
11. ✅ `AddAcademicsAndExamProctors` — ExamProctors (PK ExamId+UserId, index UserId); unique Departments(Name) and Courses(DeptId, Code); Course FK moved from shadow `DepartmentDeptId` to `DeptId` (data copied first, Restrict). Check for duplicate department names / course codes before applying.

Every migration: tested on a copy of the Neon DB first (`pg_dump` → local docker `Infra/docker-compose.postgres.yml`).

---

## 8. New / changed API surface (summary)

| Method & route | Role | Module |
|----------------|------|--------|
| `POST /api/exams/{id}/readiness` | Student | 2 |
| `GET /api/exams/{id}/readiness` | Staff | 2 |
| `POST /api/sections/{id}/attendance/import` | Teacher/Admin | 9 |
| `GET /api/sections/{id}/attendance` | Teacher/Admin | 9 |
| `POST /api/exams/{id}/eligibility/recalculate` | Teacher | 9 |
| `PUT/DELETE /api/labs/{labId}/workstations/{id}`, `GET /api/labs/unregistered-machines` | Admin | 9 |
| `POST /api/monitoring/screenshot` | Student | 5 |
| `POST /api/monitoring/events/batch` | Student | 5 |
| `GET /api/exams/{id}/sessions/{sid}/screenshots` | Staff (owner/proctor) | 5/10 |
| `POST/PATCH /api/incidents` | Teacher/Invigilator | 10 |
| hub `UnlockSession`, ✅ `ExtendStudentTime`, ✅ `ForceSubmitSessionById` | Staff (supervising the exam) | 10 / 6 |
| ✅ `POST /api/exams/sessions/{sid}/save-answer` (+ `revision`, `clientSavedAt`) | Student | 6 |
| ✅ `POST /api/exams/sessions/{sid}/submit` → receipt `{submittedAt, answerCount, sha256}` | Student | 6 |
| ✅ `GET /api/exams/sessions/{sid}/answers` | Student (own) | 6 |
| `GET /api/exams/{id}/plagiarism/matrix` | Teacher | 11 |
| `GET /api/audit-logs/verify` | Admin | 12 |
| `POST /api/analytics/reports/{type}` (`attendance|violations|scores|evidence-zip|excel`) | Staff | 12 |
| `POST /api/auth/refresh` (real) | Any | 1 |

Error codes are returned in the existing `ApiEnvelope` shape: `READINESS_REQUIRED`, `EXAM_NOT_STARTED`, `SEAT_MISMATCH`, `DESKTOP_CLIENT_REQUIRED`, `NOT_ELIGIBLE`, `EXAM_CLOSED`, `STALE_REVISION`.

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
