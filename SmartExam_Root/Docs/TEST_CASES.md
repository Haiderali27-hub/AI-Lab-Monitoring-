# SmartExam — Test Case Catalog

Companion to [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md). Paste-ready for thesis Chapter 7.

**ID format:** `M<module>-<layer>-<nn>`
| Layer | Meaning | Tool / location |
|-------|---------|-----------------|
| `U` | Unit (pure logic) | xUnit — `Backend_API.Tests` / `Student_Desktop_App.Tests` |
| `I` | API integration (in-process) | xUnit + `WebApplicationFactory` — `Backend_API.Tests/Tests/Acceptance` |
| `A` | Headless API (black-box HTTP) | Playwright `request` — `Admin_Web_Panel/tests/api` |
| `E` | Headless browser E2E | Playwright chromium + student simulator — `Admin_Web_Panel/tests/e2e` |
| `D` | Desktop UI automation | FlaUI — `Student_Desktop_App.UITests` |
| `M` | Manual lab check | Checklist, run before each demo |
| `P` | Performance | k6 |

**Status:** ✅ automated & passing · ⏳ automated as pending (feature not built; `test.fixme` / `Skip`) · 📋 specified only

Seed accounts (Development/E2E): `admin@smartexam.com / Admin@123`, `teacher@smartexam.com / Teacher@123`, `ali@smartexam.com / Student@123`, `sara@smartexam.com / Student@123`.

---

## SEC — Security hardening (done in `768df71`)

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| SEC-U-01 | PBKDF2 hash round-trip | Hash password, verify right/wrong | Prefix `pbkdf2$`; right=true, wrong=false | ✅ xUnit |
| SEC-U-02 | Legacy SHA-256 still verifies | Verify legacy hash | true; `NeedsRehash`=true | ✅ xUnit |
| SEC-I-01 | Login upgrades legacy hash | Set admin hash to legacy, login | Stored hash now `pbkdf2$` | ✅ xUnit |
| SEC-I-02 | Swagger hidden in Production | GET `/swagger/v1/swagger.json` in Production | 404 | ✅ xUnit |
| SEC-I-03 | Reset endpoint hidden in Production | Admin POST `/api/admin/reset-demo-data` in Production | 404 | ✅ xUnit |
| SEC-I-04 | Login rate limit | Limit 3/min; 4 failed logins from one IP | 4th → 429 (even with correct password); other IP → 200 | ✅ xUnit (replaces SEC-A-01: the E2E backend raises the limit) |
| SEC-A-02 | Unauthenticated access blocked | GET `/api/users`, `/api/exams`, `/api/audit-logs` without token | 401 each | ✅ Playwright api |
| SEC-A-03 | Student cannot call staff endpoints | Student token → GET `/api/users`, POST `/api/exams` | 403 | ✅ Playwright api |
| SEC-A-04 | Logout revokes token | Login, logout, reuse token on `/api/auth/me` | 401 | ✅ Playwright api |
| SEC-A-05 | Answer key hidden from students | Student GET exam detail | No `correctOptionIndex`, no hidden test cases | ✅ Playwright api |
| SEC-A-06 | Exam detail leaks no credentials | Any role GET `/api/exams/{id}` | No `passwordHash` / `salt` anywhere in the body | ✅ Playwright api + xUnit (bug found & fixed) |
| SEC-I-05 | Exam detail only for assigned students | Unassigned student GET `/api/exams/{id}` | 404 | ✅ xUnit |
| SEC-E-01 | Role routing guard | Student opens `/admin/users` | Redirected away, admin page not rendered | ✅ Playwright e2e |

## M1 — Authentication & Device Binding

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M1-A-01 | Staff login success | POST `/api/auth/login` valid creds | 200 + token + role | ✅ |
| M1-A-02 | Wrong password | Invalid password | 401, audit `LOGIN_FAILED` | ✅ |
| M1-A-03 | First student login binds device | `student-login` with HWID X | 200, `deviceBound=true` | ✅ |
| M1-A-04 | Device mismatch blocked | Same student, HWID Y | 401 `DEVICE_MISMATCH` | ✅ |
| M1-A-05 | Admin reset binding allows new device | Admin DELETE binding, login with HWID Y | 200 | ✅ |
| M1-A-06 | Missing HWID rejected | `student-login` without fingerprint | 400 | ✅ |
| M1-E-01 | Login page — valid admin | Fill form | Lands on `/admin/dashboard` | ✅ |
| M1-E-02 | Login page — invalid creds | Fill wrong password | Error shown, stays on `/login` | ✅ |
| M1-I-01 | Refresh token issued | Login | Response has non-dummy refresh token | ⏳ |
| M1-I-02 | Refresh rotates token | POST `/refresh` | New access + new refresh; old refresh invalid | ⏳ |
| M1-I-03 | Refresh reuse detection | Use old refresh again | 401, whole token family revoked | ⏳ |
| M1-I-04 | Access token lifetime 30 min | Decode JWT | `exp - iat` = 1800 s | ⏳ |
| M1-I-05 | Login by roll number | `student-login` with roll number | 200 | ⏳ |
| M1-I-06 | Duplicate names don't collide | Two students named "Ali", login by name | Rejected / requires email or roll no. | ⏳ |
| M1-I-07 | HWID v2 migration | Old-hash binding logs in with new fingerprint format | Accepted once, binding rehashed, audited | ⏳ |
| M1-I-08 | Deactivated user blocked | Deactivate, login | 401 | ✅ (existing xUnit) |

## M2 — Pre-Exam Verification & Mock Test

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M2-U-01 | Network check pass/fail | Fake health latency 300 ms / 3000 ms | pass / fail | ✅ desktop xUnit |
| M2-U-02 | Clock skew check | Skew 10 s / 120 s | pass / fail | ✅ |
| M2-U-03 | Camera check skipped when not required | `RequireWebcam=false` | Item = skipped, overall can pass | ✅ |
| M2-U-04 | Multi-monitor fails secure mode | 2 screens + `BlockMultiMonitor` | fail with detail | ✅ |
| M2-U-05 | RDP session fails secure mode | TerminalServerSession=true | fail (also VM, keyboard-hook failure) | ✅ |
| M2-U-06 | Overall result aggregation | Mixed results | `PassedAll` only if all required pass | ✅ |
| M2-U-07 | Disk space check | 199 MB / 200 MB free | fail / pass | ✅ |
| M2-U-08 | Real probes on this PC | Run every check via `ReadinessCheckService` | 7 rows, no probe throws, disk passes | ✅ |
| M2-I-01 | Submit readiness result | Student POST readiness (all pass) | 200, row stored, audit `READINESS_PASSED` | ✅ |
| M2-I-02 | Start blocked without readiness | `RequireReadiness=true`, no check | 400 `READINESS_REQUIRED` | ✅ |
| M2-I-03 | Start allowed after passing check | Pass then start | 200 | ✅ |
| M2-I-04 | Failed check doesn't unlock start | Post failed check, start | 400 | ✅ |
| M2-I-05 | Stale check (>30 min) | Check at T-31 min | 400 | ✅ |
| M2-I-06 | Staff can read readiness | Teacher GET readiness | Per-student latest | ✅ |
| M2-I-07 | Mock exam excluded from analytics | Submit mock | Not in results/analytics/plagiarism | ✅ |
| M2-I-08 | Mock exam multiple attempts | Start mock twice after submit | New session each time | ✅ |
| M2-I-09 | Desktop start path gated + status flag | `student/current`, `student/start` | 400 → pass → 200; `readinessPassed` true | ✅ |
| M2-I-10 | Required webcam can't be skipped | `RequireWebcam`, client claims pass without Camera row | `passedAll=false`, `missingRequired=[Camera]` | ✅ |
| M2-I-11 | Readiness for unassigned exam | POST for another student's exam | 404 | ✅ |
| M2-I-12 | Practice listed separately | `GET student/practice`; `student/current` | Mock listed with attempts; never the current exam | ✅ |
| M2-A-01 | Readiness endpoint exists & is student-only | Teacher POSTs readiness | 403 | ✅ |
| M2-A-02 | Desktop start path gated | `student/current` → `student/start` before/after a pass | 400 `READINESS_REQUIRED`, then 200; `readinessPassed` flips | ✅ |
| M2-E-01 | Teacher sees readiness column | Simulator posts readiness → Eligibility page | ✓ shown for that student | ✅ |
| M2-E-02 | Failed readiness visible | Simulator posts failing camera | ✗ with detail tooltip | ✅ |
| M2-E-03 | Create exam: readiness toggles | Teacher creates exam with RequireWebcam | Saved & shown on edit | ✅ |
| M2-E-04 | Practice exam labelled | Student portal exams list | Mock shows "Practice" badge | ✅ |
| M2-D-01 | Readiness checklist renders | Open dashboard for exam | All check rows visible with status | 📋 FlaUI |
| M2-D-02 | Retry single failed check | Click retry on Network | Only that item re-runs | 📋 |
| M2-D-03 | Start disabled until pass | Fail a check | Start button disabled | 📋 |
| M2-M-01 | Real webcam detected | Lab PC with webcam | Camera ✓ | 📋 manual |
| M2-M-02 | Webcam unplugged | Unplug, run | Camera ✗ "No device" | 📋 |
| M2-M-03 | Mic muted | Mute mic | Mic ✗ low level (not built: presence-only check today) | 📋 |
| M2-M-04 | Second monitor attached | Plug monitor | Secure mode ✗ | 📋 |
| M2-M-05 | Network cable pulled | Unplug LAN | Network ✗ | 📋 |

## M3 — Student Exam Dashboard

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M3-A-01 | Current exam status | Student GET `/api/exams/student/current` | Exam, remainingSeconds > 0, isEligible | ✅ |
| M3-I-01 | Start before StartTime blocked | Exam starts in +1 h; start-session | 400 `EXAM_NOT_STARTED` | ⏳ |
| M3-I-02 | Early-join window honoured | `EarlyJoinMinutes=5`, start at T-3 min | 200 | ⏳ |
| M3-I-03 | Start after end blocked | Exam ended | 400 | ✅ (since `768df71`) |
| M3-I-04 | Ineligible blocked | IsEligible=false | 400 | ✅ |
| M3-E-01 | Student portal lists exam | Login as student in web portal | Exam card visible | ✅ |
| M3-E-02 | Student result page | After submit + grading | Result page shows marks | ✅ |
| M3-E-03 | Countdown not available before start | Future exam | Portal shows "starts in" not "remaining" | ⏳ |
| M3-D-01 | Start button disabled before start time | Future exam in WPF | Disabled with message | 📋 |
| M3-D-02 | Countdown resync after extend | Teacher extends | WPF timer jumps within 1 heartbeat | 📋 |

## M4 — Secure Exam Environment

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M4-U-01 | Alt+Tab blocked | Policy.ShouldBlockKey(Alt+Tab) | true | ✅ desktop xUnit |
| M4-U-02 | Win key blocked | LWin/RWin/Menu | true | ✅ |
| M4-U-03 | Alt+F4 blocked | | true | ✅ |
| M4-U-04 | Ctrl+Esc / Ctrl+Shift+Esc blocked | | true | ✅ |
| M4-U-05 | PrintScreen blocked | | true | ✅ |
| M4-U-06 | Normal typing allowed | Letters, Ctrl+C inside app, arrows, Tab | false | ✅ |
| M4-U-07 | Allowed app passes | `code.exe` in AllowedApps | Allowed | ✅ |
| M4-U-08 | System process whitelisted | `csrss.exe`, `explorer.exe`, own exe; windowless background process | Allowed | ✅ |
| M4-U-09 | Unknown app in Enforce mode | `chrome.exe`, `notepad.exe`; `anydesk` even without a window | Kill + report | ✅ |
| M4-U-10 | Unknown app in Report mode | `chrome.exe` | Report only; teacher can whitelist it | ✅ |
| M4-U-11 | AllowedApps matching is case/extension/path-insensitive | `Code.EXE`, `C:\…\code.exe` vs `code` | Allowed | ✅ |
| M4-U-12 | Foreground check uses PID, not title | Notepad titled "Exam" | Not treated as exam window | ✅ |
| M4-U-13 | Violation classifier | Warnings ×3 → Suspicious; Suspicious ×2 → Violation; ProcessKilled/USB/RDP → Violation; `"Violation"+reason` resolves to the specific type | As described | ✅ backend xUnit |
| M4-U-14 | Virtual machine detection | WMI manufacturer/model of VMware, VirtualBox, Hyper-V, QEMU vs Dell/Surface | Vendor name / null | ✅ desktop xUnit |
| M4-I-01 | Lockdown config in student status | Exam LockdownMode=Enforce | `/student/current` returns mode + allowedApps | ✅ |
| M4-I-02 | Violation types accepted | POST event `UnauthorizedProcess` | 200, stored with type | ✅ |
| M4-I-03 | Severity classification | 1 FocusLost → Warning; 3 → Suspicious; ProcessKilled → Violation | Tile status matches | ✅ |
| M4-I-04 | Violation on other student's session rejected | Student A posts to B's session | 404 | ✅ |
| M4-I-05 | Violations listed for student | GET `/api/student/violations` | Contains posted violation | ✅ |
| M4-E-01 | Violation appears live | Simulator posts violation → teacher live monitor (headless) | Tile turns red within 5 s | ✅ |
| M4-E-02 | Violation type label shown | Simulator posts `KeyBlocked` then `UnauthorizedProcess` | Tile shows "Blocked keys: Alt+Tab", then "Unauthorized app: chrome (Killed)" | ✅ |
| M4-E-03 | Create exam lockdown settings | Wizard: set Enforce, untick multi-monitor | Persisted; edit page shows them | ✅ |
| M4-E-04 | Student violations page | Student portal `/student/violations` | Lists simulator's violation | ✅ |
| M4-D-01 | Exam window fullscreen & topmost | Start exam | Covers screen, no taskbar | 📋 FlaUI |
| M4-D-02 | Close button / Alt+F4 ignored | Send Alt+F4 | Window stays | 📋 |
| M4-D-03 | Window closes after submit | Submit | Window closes, lockdown released | 📋 |
| M4-D-04 | Minimise impossible | Send minimise | Stays maximised | 📋 |
| M4-D-05 | Unauthorized app killed | Launch notepad during exam | Process exits, violation sent | 📋 |
| M4-D-06 | Clipboard cleared | Copy text in notepad before exam, paste in exam | Empty | 📋 |
| M4-D-07 | Teardown after crash | Kill exam process | Next launch restores Task Manager policy | 📋 |
| M4-D-08 | Hooks removed after exam | After submit press Alt+Tab | Works normally | 📋 |
| M4-M-01 | Ctrl+Alt+Del | Press during exam | Focus-loss violation logged on return | 📋 manual |
| M4-M-02 | Task Manager | Ctrl+Shift+Esc | Blocked / does not open | 📋 |
| M4-M-03 | USB stick inserted | Insert USB | `UsbInserted` violation on live monitor | 📋 |
| M4-M-04 | Second monitor hot-plug | Plug monitor mid-exam | `MultiMonitor` violation | 📋 |
| M4-M-05 | RDP into lab PC | Start exam over RDP | Blocked at readiness | 📋 |
| M4-M-06 | Antivirus doesn't quarantine exe | Defender scan | Clean / documented exclusion | 📋 |

## M5 — Live Monitoring Client Service

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M5-U-01 | Offline queue preserves order | Enqueue 5, replay | Same order, same timestamps | 📋 desktop |
| M5-U-02 | Offline queue encrypted at rest | Inspect file | Not plaintext JSON | 📋 |
| M5-U-03 | Queue survives restart | Enqueue, dispose, reopen | Items still present | 📋 |
| M5-U-04 | Process diff | Lists A then A+B | Diff = +B | 📋 |
| M5-U-05 | Screenshot scaling | 3840×2160 capture | ≤1280 px wide JPEG | 📋 |
| M5-U-06 | Batch flush every 5 s | Fake clock | Flushes at 5 s boundaries | 📋 |
| M5-A-01 | Heartbeat accepted | Student POST `/api/monitoring/heartbeat` | 200 | ✅ |
| M5-A-02 | Heartbeat for foreign session rejected | Other student's session id | Not attributed to that session ✅; status is 200, not 403/404 (`test.fail` until changed) | ⚠️ partial |
| M5-I-01 | Screenshot upload | POST multipart jpg | 200, row + file stored | ⏳ |
| M5-I-02 | Screenshot size limit | 10 MB upload | 413 | ⏳ |
| M5-I-03 | Non-image rejected | Upload .exe renamed .jpg | 400 | ⏳ |
| M5-I-04 | Screenshot access control | Teacher of other section GETs screenshots | 403 | ⏳ |
| M5-I-05 | Batch events | POST 20 events | 200, 20 stored | ⏳ |
| M5-I-06 | ClientEventId dedup | Same batch twice | Stored once | ⏳ |
| M5-I-07 | Original timestamps kept | Replay offline events | `RecordedAt` = client time | ⏳ |
| M5-I-08 | Disconnected detection | No heartbeat 30 s | Session flagged Disconnected, hub broadcast | ⏳ |
| M5-I-09 | Retention cleanup | Screenshot older than RetentionDays | Deleted by job | ⏳ |
| M5-E-01 | Heartbeat shows student online | Simulator heartbeats → live monitor | Tile shows student, Normal | ✅ |
| M5-E-02 | Thumbnail on tile | Simulator uploads screenshot | Tile shows image | ⏳ |
| M5-E-03 | Disconnected tile | Simulator stops heartbeats | Tile shows "Disconnected" within 40 s | ⏳ |
| M5-D-01 | Screenshot every interval | 30 s interval, 2 min | ~4 uploads | 📋 |
| M5-D-02 | Network drop & recover | Disable NIC 60 s | Events replayed, none lost | 📋 |
| M5-D-03 | Foreground app reported | Switch to allowed app | Heartbeat shows its process name | 📋 |
| M5-P-01 | 30 clients per lab | k6: 30 virtual students heartbeat 10 s + events | p95 < 2 s, 0 errors | 📋 k6 |

## M6 — Submission & Backup

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M6-U-01 | Autosave timer only when dirty | No edits 30 s | 0 saves | 📋 desktop |
| M6-U-02 | Autosave fires at 10 s | Edit, wait 10 s | 1 save | 📋 |
| M6-U-03 | Local backup round-trip | Write, read | Same answers | 📋 |
| M6-U-04 | Backup tamper detected | Flip byte | HMAC fails, backup ignored + reported | 📋 |
| M6-U-05 | Submit receipt hash | Same answers | Same SHA-256 as server | 📋 |
| M6-A-01 | Save answer in window | save-answer | 200 | ✅ |
| M6-A-02 | Save after deadline rejected | Exam past end + grace | 400 | ✅ |
| M6-A-03 | Save after submit rejected | Submit then save | 400 | ✅ |
| M6-A-04 | Double submit rejected | Submit twice | 2nd → 400 | ✅ |
| M6-I-01 | Stale revision ignored | Save rev 3 then rev 2 | Answer = rev 3, 409 `STALE_REVISION` | ⏳ |
| M6-I-02 | Submit receipt returned | Submit | `{submittedAt, answerCount, sha256}` | ⏳ |
| M6-I-03 | Timeout job | Session InProgress 2 min after end | Status TimedOut, graded | ⏳ |
| M6-I-04 | Force-submit-all grades | Teacher force-submit-all | AiGradingResults created | ✅ (since `768df71`) |
| M6-I-05 | Hub force-submit one student grades | Hub `ForceSubmitSession` | Graded | ✅ |
| M6-I-06 | Late submit still locks | Submit after deadline | 200, status Submitted, no new answers | ✅ |
| M6-I-07 | Per-student extra time | ExtraMinutes=10 | Save allowed until end+10 | ⏳ |
| M6-I-08 | Answers immutable after submit | Teacher views answers | Unchanged | ✅ |
| M6-I-09 | Restore resumes session | Start, kill client, start again | start-session returns same session | ✅ |
| M6-E-01 | Results show submitted student | Simulator submits → teacher results page | Student row with score | ✅ |
| M6-E-02 | Force submit all from UI | Teacher clicks Force Submit All | Simulator's next save → 400 | ✅ |
| M6-E-03 | Per-student force submit button | Drawer → Force submit | Tile shows Submitted | ⏳ |
| M6-E-04 | Receipt hash on results page | | Matches simulator receipt | ⏳ |
| M6-D-01 | Crash recovery | Kill WPF mid-exam, relaunch | Answers restored | 📋 |
| M6-D-02 | Offline answering | Pull LAN, answer 3 Qs, reconnect | All 3 on server | 📋 |
| M6-D-03 | Auto-submit at 0 | Short exam | Submits automatically | 📋 |
| M6-D-04 | Receipt dialog | Submit | Shows hash | 📋 |

## M7 — User & Role Management

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M7-A-01 | Create user | Admin POST `/api/users` | 201 | ✅ |
| M7-A-02 | Duplicate email rejected | Same email again | 400/409 | ✅ |
| M7-A-03 | Teacher cannot create users | Teacher POST | 403 | ✅ |
| M7-A-04 | Deactivate kills sessions | Deactivate, use old token | 401 | ✅ |
| M7-E-01 | Users page lists users | Admin → Users | Table with seeded users | ✅ |
| M7-E-02 | Create user via UI | Fill dialog | Row appears | ✅ |
| M7-E-03 | Audit log shows login | Admin → Audit Logs | `LOGIN_SUCCESS` row | ✅ |
| M7-E-04 | Invigilator UI route | Login as invigilator | Lands on `/invigilator/live-monitor` | ⏳ |
| M7-I-01 | CSV import | POST import 3 rows | 3 created, bad rows reported | ✅ |
| M7-I-02 | Departments CRUD | POST/GET/PUT/DELETE departments | Works | ⏳ |
| M7-I-03 | Courses CRUD | Same for courses | Works | ⏳ |
| M7-I-04 | SuperAdmin manages admins | SuperAdmin creates Admin | 201; Admin cannot create SuperAdmin | ⏳ |
| M7-I-05 | Invigilator sees only assigned exams | Invigilator GET exams | Only ExamProctors rows | ⏳ |

## M8 — Exam Creation & Configuration

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M8-A-01 | Create exam via API | Teacher POST exam with MCQ+coding | 201 | ✅ |
| M8-A-02 | Invalid MCQ index | correctOptionIndex out of range | 400 | ✅ |
| M8-A-03 | Coding without test cases | | 400 | ✅ |
| M8-E-01 | Create exam wizard end-to-end | UI: details → 2 questions → publish | Exam in dashboard list | ✅ |
| M8-E-02 | Monitoring settings step | Set screenshot interval 30 s | Persisted | ⏳ |
| M8-E-03 | Allowed apps picker | Pick code.exe | `AllowedApps` contains it | ⏳ |

## M9 — Eligibility & Access Control

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M9-U-01 | Attendance % calc | 30/40 | 75% | 📋 |
| M9-U-02 | Threshold boundary | 74.9% vs 75% min | ineligible / eligible | 📋 |
| M9-U-03 | Zero classes | total=0 | Not computed, flagged | 📋 |
| M9-A-01 | Manual eligibility toggle | Teacher sets ineligible | Student start → 400 | ✅ |
| M9-I-01 | Attendance CSV import | POST CSV 2 rows | Records stored | ⏳ |
| M9-I-02 | Recalculate sets ineligible | 60% attendance, min 75 | IsEligible=false, note "Attendance 60% < 75%" | ⏳ |
| M9-I-03 | Recalculate keeps overrides | Manual override true, attendance 60% | Stays eligible | ⏳ |
| M9-I-04 | Decisions audited | Recalculate | `ELIGIBILITY_COMPUTED` per student | ⏳ |
| M9-I-05 | Seat match by machine name | Assigned R1C1, binding MachineName R1C1 | start 200 | ⏳ |
| M9-I-06 | Seat mismatch blocked | Assigned R1C1, logs in from R1C2 | 403 `SEAT_MISMATCH`, audit | ⏳ |
| M9-I-07 | Seat match by IP mode | SeatMatch=Ip | Compare request IP | ⏳ |
| M9-I-08 | No seat assigned → not enforced | WorkstationId null | 200 | ⏳ |
| M9-I-09 | Heartbeat from wrong seat flagged | Mid-exam seat change | Violation `SeatMismatch` | ⏳ |
| M9-I-10 | Auto-register off by default | Student login from new PC | No workstation auto-created | ⏳ |
| M9-E-01 | Eligibility page loads | Teacher → Eligibility | Assignments table | ✅ |
| M9-E-02 | Toggle eligibility in UI | Click toggle | Simulator start → 400 | ✅ |
| M9-E-03 | Attendance upload dialog | Upload CSV | Attendance column filled | ⏳ |
| M9-E-04 | Recalculate button | Click | Ineligible rows show reason | ⏳ |
| M9-E-05 | Seat mismatch alert | Simulator logs in from wrong machine | Alert on live monitor | ⏳ |

## M10 — Live Proctoring Dashboard

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M10-I-01 | Incident create | POST incident | 201, audit | ⏳ |
| M10-I-02 | Incident escalate/resolve | PATCH status | Transition stored, invalid transitions 400 | ⏳ |
| M10-I-03 | Incident access control | Teacher of other section | 403 | ⏳ |
| M10-I-04 | Unlock session | Hub UnlockSession 5 min | Event sent to student, audited | ⏳ |
| M10-I-05 | Extend single student | ExtendStudentTime | Only that session's end moves | ⏳ |
| M10-I-06 | Hub group isolation | Student joins exam group | HubException | ✅ (existing) |
| M10-I-07 | Teacher can't force-submit other teacher's exam | Hub call | HubException | ✅ (existing) |
| M10-I-08 | Proctor assignment | Add ExamProctor | Invigilator can join group | ⏳ |
| M10-E-01 | Live monitor loads | Teacher → Live Monitor | Page renders, no error | ✅ |
| M10-E-02 | Student tile appears on heartbeat | Simulator starts (both `start-session` and the desktop `student/start` path) + heartbeats | Tile with student name | ✅ (desktop-path bug found & fixed) |
| M10-E-03 | Status progression | KeyBlocked → UnauthorizedProcess → ProcessKilled | Stable → Warning → Suspicious → Critical | ✅ |
| M10-E-04 | Send warning | Teacher sends warning to tile | Simulator's notifications contain it | ✅ |
| M10-E-05 | Extend time | Teacher extends 10 min | Simulator `remainingSeconds` grows ~600 | ✅ |
| M10-E-06 | Detail drawer | Click tile | Timeline + process list | ⏳ |
| M10-E-07 | Mark incident | Drawer → Mark incident | Incident badge on tile | ⏳ |
| M10-E-08 | Screenshot carousel | Drawer | Simulator's screenshots | ⏳ |
| M10-E-09 | Unlock button | Drawer → Unlock 5 min | Toast + audit row | ⏳ |
| M10-E-10 | Invigilator restricted view | Invigilator → other exam | Not listed | ⏳ |

## M11 — AI-Assisted Evaluation

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M11-U-01 | MCQ correct | index matches | full marks | ✅ (via xUnit flow) |
| M11-U-02 | MCQ wrong | | 0 | ✅ |
| M11-U-03 | Test-case scoring weights | 2/3 passed, weights 1,1,2 | 50% | 📋 |
| M11-U-04 | Compile error → 0 with message | | 0, compiler output in justification | 📋 |
| M11-U-05 | Timeout counts as fail | infinite loop | that case fails, others run | 📋 |
| M11-U-06 | Output comparison trims whitespace | "5\n" vs "5" | pass | 📋 |
| M11-U-07 | TF-IDF cosine identical text | | ≈1.0 | 📋 |
| M11-U-08 | Plagiarism ignores identifier renames | Same code, renamed vars | similarity ≥ 90% | 📋 |
| M11-U-09 | Plagiarism ignores comments/whitespace | | ≥ 95% | 📋 |
| M11-U-10 | Different solutions low similarity | Two distinct algorithms | < 30% | 📋 |
| M11-I-01 | Submit returns before grading finishes | Slow fake runner | Submit 200 < 1 s, status Pending | ⏳ |
| M11-I-02 | Worker grades pending answers | Run worker | Status Graded with per-case results | ⏳ |
| M11-I-03 | Hidden test results not shown to student | Student result | Only visible cases detailed | ⏳ |
| M11-I-04 | Sandbox has no network | Code that opens socket | Fails, no egress | ⏳ (Docker) |
| M11-I-05 | Sandbox memory limit | Allocates 1 GB | Killed, case fails | ⏳ (Docker) |
| M11-I-06 | Plagiarism matrix | 3 students | 3×3 symmetric matrix | ⏳ |
| M11-I-07 | Matched segments line ranges | | Ranges present | ⏳ |
| M11-I-08 | Teacher override wins | Override after AI grade | Final mark = override | ✅ |
| M11-A-01 | Heuristic grading labelled honestly | Submit coding answer | Confidence Low + "teacher review required" | ✅ |
| M11-A-02 | Plagiarism pair flagged | Two simulators submit identical theory answers | Pair in plagiarism list | ✅ |
| M11-E-01 | Results page shows AI suggestion | Teacher → results | Suggested marks + justification | ✅ |
| M11-E-02 | Override in UI | Edit marks | Saved, shown as Overridden | ✅ |
| M11-E-03 | Plagiarism heat map | Analytics page | Heat map rendered | ⏳ |
| M11-E-04 | Test-case breakdown | Results drawer | Per-case pass/fail | ⏳ |

## M12 — Logs, Analytics & Audit Trail

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M12-U-01 | Hash chain computation | 3 entries | Each Hash = SHA256(prev‖fields) | 📋 |
| M12-U-02 | Verify detects modified row | Change Details of row 2 | Broken at row 2 | 📋 |
| M12-U-03 | Verify detects deleted row | Remove row 2 | Broken at row 3 (sequence gap) | 📋 |
| M12-U-04 | Canonical JSON stable | Same object, different key order | Same hash | 📋 |
| M12-A-01 | Audit logs admin-only | Teacher GET audit-logs | 403 | ✅ |
| M12-A-02 | Exam summary analytics | Teacher GET summary | Totals present | ✅ |
| M12-A-03 | PDF report generate + download | POST generate, GET download | `application/pdf`, starts `%PDF` | ✅ |
| M12-A-04 | Report download path traversal | download `../../appsettings.json` | 400/404 | ✅ |
| M12-I-01 | Verify endpoint OK | GET verify on clean chain | `{valid:true}` | ⏳ |
| M12-I-02 | Verify endpoint broken | Tamper row in DB | `{valid:false, brokenAt}` | ⏳ |
| M12-I-03 | Concurrent writes keep chain | 50 parallel audit writes | Chain valid | ⏳ |
| M12-I-04 | Attendance report | POST reports/attendance | PDF | ⏳ |
| M12-I-05 | Violations report | | PDF with evidence thumbnails | ⏳ |
| M12-I-06 | Scores Excel | reports/excel | `.xlsx` with one row per student | ⏳ |
| M12-I-07 | Evidence ZIP manifest | reports/evidence-zip | ZIP with manifest SHA-256 matching files | ⏳ |
| M12-I-08 | App-switch events audited | Violation posted | Visible in audit/events | ⏳ |
| M12-I-09 | Backfill existing rows | Run migration job | Chain valid over old rows | ⏳ |
| M12-E-01 | Audit logs page | Admin → Audit Logs | Rows + filters | ✅ |
| M12-E-02 | Reports page generates PDF | Teacher → Reports → Generate | Download link appears | ✅ |
| M12-E-03 | System analytics page | Admin → Analytics | Charts render | ✅ |
| M12-E-04 | Chain verified badge | Admin → Audit Logs | "Chain verified" | ⏳ |
| M12-E-05 | Evidence ZIP download | Reports → Evidence | Download starts | ⏳ |

---

## Defects found by the suites

| # | Found by | Defect | Status |
|---|----------|--------|--------|
| D-01 | SEC-A-05 | `GET /api/exams/{id}` serialised the raw EF graph incl. every assigned student's `PasswordHash`/`Salt`, to any logged-in user; students could read any exam | **Fixed** — explicit projection + assignment check (SEC-A-06, SEC-I-05) |
| D-02 | M10-E-02 | `POST /api/exams/student/start` (desktop path) never set the exam Active → Live Monitor showed "No Exam Currently Active" | **Fixed** |
| D-03 | M10-E-03 | No Warning/Suspicious states; any violation turned the tile red | **Fixed** — `ViolationClassifier` (M4-I-03) |
| D-04 | M10-E-* | Live Monitor shows only the first Active exam; no exam picker | Open (P2, Module 10) |
| D-05 | M10-E-04 | Hub `SendWarning` only stores a notification — nothing is pushed live to the student client | Open (P2, Module 10) |
| D-06 | M5-A-02 | Heartbeat naming another student's session returns 200 (ignored, not attributed) instead of 403/404 | Open (low) |
| D-07 | review | Live Monitor logged "Window Focus Lost" whenever the window title wasn't "Exam Browser" | **Fixed** — uses the client's foreground flag |

---

## Running the suites

```powershell
# Backend unit + integration (in-memory DB)
cd SmartExam_Root; dotnet test SmartExam.sln

# Headless API + browser E2E (starts backend in E2E in-memory mode + Vite automatically)
cd SmartExam_Root\Admin_Web_Panel
npx playwright install chromium      # first time only
npm run test:api
npm run test:e2e
npm run test:report                  # open HTML report
```
