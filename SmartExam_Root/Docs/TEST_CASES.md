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

Seed accounts (Development/E2E): `admin@smartexam.com / Admin@123`, `teacher@smartexam.com / Teacher@123`, `ali@smartexam.com / Student@123`, `sara@smartexam.com / Student@123`, `root@smartexam.com / Super@123` (SuperAdmin), `invigilator@smartexam.com / Invig@123` (proctors the open demo exam).

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
| M1-I-01 | Refresh token issued | Login | Response has non-dummy refresh token | ✅ xUnit |
| M1-I-02 | Refresh rotates token | POST `/refresh` | New access + new refresh; old refresh invalid | ✅ xUnit |
| M1-I-03 | Refresh reuse detection | Use old refresh again | 401, whole token family revoked | ✅ xUnit |
| M1-I-04 | Access token lifetime 30 min | Decode JWT | `exp - iat` = 1800 s | ✅ xUnit |
| M1-I-05 | Login by roll number | `student-login` with roll number | 200 | ✅ xUnit |
| M1-I-06 | Duplicate names don't collide | Two students named "Ali", login by name | Rejected / requires email or roll no. | ✅ xUnit |
| M1-I-07 | HWID v2 migration | Old-hash binding logs in with new fingerprint format | Accepted once, binding rehashed, audited | ✅ xUnit |
| M1-I-08 | Deactivated user blocked | Deactivate, login | 401 | ✅ (existing xUnit) |
| M1-I-09 | Refresh revocation paths | Logout / force-logout / deactivate, then refresh; refresh for an inactive user; expired token | 401 each (`REFRESH_TOKEN_EXPIRED` for expiry); a revoked (not rotated) token is not treated as theft | ✅ xUnit (`RefreshTokenAndRollNumberTests`) |
| M1-I-10 | Refresh keeps seat claims, never extends the family | Desktop login with machine name → refresh | New token still has `client=desktop` + `machine`; successor `ExpiresAt` = original; only the SHA-256 hash is stored | ✅ xUnit |
| M1-I-11 | Roll-number management | Set " bscs-21-001 ", reuse it, omit it, clear it, import duplicates | Stored `BSCS-21-001`; duplicate → 409; omitted = unchanged; `""` clears; import skips duplicate roll | ✅ xUnit |
| M1-I-12 | HWID v2 upgrade rule | Wrong legacy proof; v2 without proof; second v2 on a v2 binding; first bind with v2 | 401 `DEVICE_MISMATCH` (no rebind, no audit); first bind stores `HwidVersion=2` | ✅ xUnit |
| M1-I-13 | Session revocation on logout / password change | Rotate then logout; change own password with a second live login; wrong current password; admin password reset; admin edit without password | Logout kills earlier access tokens of the family too; password change revokes every other refresh family + access token (`password_changed`), keeps the caller's; wrong password revokes nothing; admin reset revokes all of the user's sessions, admin's own untouched; plain edit revokes nothing | ✅ xUnit (verifier) |
| M1-A-07 | Refresh rotation + reuse over HTTP | Simulator: login → refresh → replay old refresh | 200 then 401 `REFRESH_TOKEN_REUSED`; successor and its access token dead; `REFRESH_TOKEN_REUSE` audited | ✅ Playwright api |
| M1-A-08 | Logout / force-logout revoke refresh | Logout, refresh; login, admin force-logout, refresh | 401 both | ✅ Playwright api |
| M1-A-09 | Staff refresh + 30-min access token | Staff login, decode JWT, refresh | `exp - iat` = 1800; refresh 200 with role | ✅ Playwright api |
| M1-A-10 | Roll-number login, not display name | Admin sets roll; login by roll / by name; reuse roll | 200 / 401 / 409 | ✅ Playwright api |
| M1-E-03 | Web refresh interceptor | Logged-in admin's access token replaced by an invalid one → open Users | Page loads (single refresh + retry); tokens rotated in localStorage | ✅ Playwright e2e |
| M1-E-04 | Rejected refresh → login | Invalid access + unknown refresh token → open Users | Redirected to `/login`, session cleared | ✅ Playwright e2e |
| M1-E-05 | Web logout revokes server-side | Logout button, then POST `/refresh` with the old refresh token | 401 | ✅ Playwright e2e |
| M1-E-06 | Roll number in Users page | Admin creates student with roll number (lower-case) | Shown upper-case, searchable; desktop-style login by roll → 200 | ✅ Playwright e2e |
| M1-E-07 | Logout after idle expiry | Access token in localStorage already expired → Logout button | Logout refreshes first and reaches the server with a valid token (200); the newest refresh token is then `INVALID_REFRESH_TOKEN` | ✅ Playwright e2e (verifier) |
| M1-U-01 | Desktop refresh is single-flight | Two concurrent 401s; stale failure after another refresh; near-expiry token | One `POST /refresh`; both callers get the new pair; proactive refresh 60 s before expiry | ✅ desktop xUnit (`TokenRefreshGateTests`) |
| M1-U-02 | Desktop keeps the session on transient errors | Refresh → network error / 429 / 5xx vs `REFRESH_TOKEN_REUSED` | Transient: keep token, no kick; refusal: session over; logout during refresh is not undone | ✅ desktop xUnit |
| M1-U-03 | HWID v2 fingerprint composition | BIOS/disk/board serials incl. OEM junk ("To be filled by O.E.M.", "0", …) | `v2\|BIOS\|DISK\|BOARD`; junk dropped; < 2 usable serials → SMBIOS UUID, else MachineGuid appended | ✅ desktop xUnit (`DeviceFingerprintTests`) |

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
| M2-U-08 | Real probes on this PC | Run every check via `ReadinessCheckService` | 7 rows, no probe throws, disk passes (with `RequireWebcam` the camera is really opened through OpenCV and a frame grabbed) | ✅ |
| M2-U-09 | Webcam check needs a real picture | `EvaluateCamera`: no device / no frame / dark or featureless frame / good frame | fail / fail / fail "dark or blocked" / pass (S5: replaces device enumeration only) | ✅ desktop xUnit |
| M2-U-10 | Webcam check needs exactly one face (S8) | `EvaluateCamera` with the face model's count 1 / 0 / 3 / not run; covered lens with 1 face; count uses the monitoring size rule | pass "face detected" / fail "No face visible" / fail "3 faces visible" / pass "picture OK" (no model on the PC); covered still fails; a small background face is not counted | ✅ desktop xUnit |
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
| M3-A-01 | Current exam status | Student GET `/api/exams/student/current` | Exam, remainingSeconds > 0, isEligible, `canStartNow`, `secondsUntilStart=0` | ✅ |
| M3-A-02 | Start before StartTime refused (black-box) | Exam at +30 min; start-session and student/start | Both 400 `EXAM_NOT_STARTED`; status `canStartNow=false` | ✅ |
| M3-A-03 | Early join (black-box) | `earlyJoinMinutes=5`, start at T-3 | `canStartNow=true`, start 200 | ✅ |
| M3-I-01 | Start before StartTime blocked | Exam starts in +1 h; start-session | 400 `EXAM_NOT_STARTED` | ✅ |
| M3-I-02 | Early-join window honoured | `EarlyJoinMinutes=5`, start at T-3 min | 200 | ✅ |
| M3-I-03 | Start after end blocked | Exam ended | 400 | ✅ (since `768df71`) |
| M3-I-04 | Ineligible blocked | IsEligible=false | 400 | ✅ |
| M3-I-05 | Desktop start path codes | student/start before open / inside early-join / after end | `EXAM_NOT_STARTED` / 200 / `EXAM_CLOSED` (replaces `OUTSIDE_EXAM_WINDOW`) | ✅ |
| M3-I-06 | Start gate reported on server clock | GET student/current for exam at +20 min, early join 5 | `secondsUntilStart`≈15 min, `opensAtUtc`, `serverTimeUtc`, `canStartNow=false` | ✅ |
| M3-I-07 | Early-join validation | `earlyJoinMinutes` −1 / 61; 10 | 400 / round-trips on exam detail | ✅ |
| M3-E-01 | Student portal lists exam | Login as student in web portal | Exam card visible | ✅ |
| M3-E-02 | Student result page | After submit + grading | Result page shows marks | ✅ |
| M3-E-03 | Countdown not available before start | Future exam | Portal shows "starts in" not "remaining" | ✅ |
| M3-D-01 | Start button disabled before start time | Future exam in WPF | Disabled; clock shows "STARTS IN"; unlocks at the opening on the server clock | ✅ logic (`StartGateTests`, M3_D_01_*); FlaUI 📋 |
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
| M4-U-15 | Escape tools caught without a window | `taskmgr`, `procexp64`, `SystemInformer`, `regedit` windowless; windowless `cmd`/`powershell` (IDE terminals) | Enforce → Kill, Report → Report; shells allowed unless windowed; teacher can whitelist | ✅ desktop xUnit |
| M4-U-16 | Task Manager policy apply / restore | Fake registry: value absent / 0 / 1 (GPO); marker write fails; registry write fails; apply twice | Marker written before `DisableTaskMgr=1`; restore deletes or rewrites the original; idempotent; no registry change without a marker | ✅ desktop xUnit (`TaskManagerPolicyTests`) |
| M4-U-17 | Task Manager crash recovery | Marker left by killed pid 42; next start as pid 77 (file marker in a temp dir) | Original value restored, marker removed; not restored while another SmartExam instance still runs; corrupt marker → default (value deleted) | ✅ desktop xUnit |
| M4-U-18 | Teardown isolation | A teardown step throws (cross-thread HwndSource in the crash / ProcessExit handler) | Later steps still run, failures reported; Task Manager policy restored first, even when a UI-bound step throws | ✅ desktop xUnit (`LockdownTeardownTests`, verifier) |
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
| M4-D-07 | Teardown after crash | Kill exam process | Next launch restores Task Manager policy | 📋 FlaUI (decision logic ✅ M4-U-16/17) |
| M4-D-08 | Hooks removed after exam | After submit press Alt+Tab | Works normally | 📋 |
| M4-M-01 | Ctrl+Alt+Del | Press during exam | Focus-loss violation logged on return | 📋 manual |
| M4-M-02 | Task Manager | Ctrl+Shift+Esc; Ctrl+Alt+Del → Task Manager | Key blocked; Task Manager "disabled by your administrator" while the exam window is up, works again after submit | 📋 |
| M4-M-03 | USB stick inserted | Insert USB | `UsbInserted` violation on live monitor | 📋 |
| M4-M-04 | Second monitor hot-plug | Plug monitor mid-exam | `MultiMonitor` violation | 📋 |
| M4-M-05 | RDP into lab PC | Start exam over RDP | Blocked at readiness | 📋 |
| M4-M-06 | Antivirus doesn't quarantine exe | Defender scan | Clean / documented exclusion | 📋 |

## M5 — Live Monitoring Client Service

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M5-U-01 | Offline queue preserves order | Enqueue 5, replay | Same order, same timestamps | ✅ desktop xUnit |
| M5-U-02 | Offline queue encrypted at rest | Inspect file | Not plaintext JSON (AES-256-GCM per record, DPAPI-wrapped key) | ✅ |
| M5-U-03 | Queue survives restart | Enqueue, dispose, reopen | Items still present; acks durable | ✅ |
| M5-U-04 | Process diff | Lists A then A+B | Diff = +B (case / `.exe` ignored) | ✅ |
| M5-U-05 | Screenshot scaling | 3840×2160 capture | ≤1280 px wide JPEG (encoded and decoded: 1280×720) | ✅ |
| M5-U-06 | Batch flush every 5 s | Fake clock | Flushes at 5 s boundaries; a violation at once | ✅ |
| M5-U-07 | Outbox tamper / torn tail | Flip a record byte, append half a record | Edited record skipped and counted, torn tail dropped, file rewritten | ✅ |
| M5-U-08 | Capture spool | Add / read / fill past the limit / edit a file | Encrypted round trip; interval captures dropped before violation evidence; an edited capture is discarded | ✅ |
| M5-U-09 | Capture schedule | Fake clock + random | Waits uniform in [0.5, 1.5]× mean (60 s → 30–90 s); mean clamped 10–600; violation capture ≤ 1 per 15 s; a shorter teacher interval reschedules | ✅ |
| M5-U-10 | Webcam frame metrics | Black, flat grey, textured, repeated frames | Mean / Laplacian variance / frame difference tell covered, taped and frozen apart | ✅ |
| M5-U-11 | Camera debounce state machine | Synthetic frame sequences | Covered after 3 s, frozen after 15 s, unavailable after 5 s, each once per episode; cleared after 2 s; a hand passing never raises; a black still image is "covered", not "frozen" | ✅ |
| M5-U-12 | Batch backoff | Fake clock, failed flushes | 5 → 10 → … → 60 s; a successful heartbeat replays at once | ✅ |
| M5-U-13 | Input idle time | Tick counts incl. a 32-bit wrap | Correct seconds | ✅ |
| M5-U-15 | Webcam loop survives camera faults (S5 verifier) | Fake camera whose grab throws `InvalidOperationException` | Thread keeps running, faults counted (was: only OpenCV/DLL exceptions caught, anything else ended the exam app) | ✅ desktop xUnit |
| M5-U-16 | Webcam loop survives throwing subscribers | Covered frames; `IssueChanged` and `SnapshotReady` handlers throw `IOException` | One episode event, snapshot attempted, thread still running, status Covered | ✅ |
| M5-U-17 | No camera / no OpenCV runtime | Open throws; source factory throws `DllNotFoundException` | Absorbed, no crash; status Off once stopped | ✅ |
| M5-U-18 | Webcam model decoders (S8) | Hand-made YuNet output tensors (3 strides) and YOLOX-Nano `[3549×85]` tensor; IoU / NMS | Box, score √(cls·obj) and 5 landmarks decoded in frame-height units; weak and duplicate faces dropped, best first; phone box mapped back through the resize ratio, score = obj × class, class-aware NMS keeps a person and a phone; outputs of another model → `ArgumentException` | ✅ desktop xUnit |
| M5-U-19 | Head pose from 5 landmarks (S8) | Generic face synthesised at known yaw/pitch/roll | Yaw and pitch within 2° (yaw ±45°, pitch ±30°, roll ±20°, upside down); faces too small (< 0.04 frame heights between the eyes) or with swapped / missing landmarks → no pose | ✅ |
| M5-U-20 | Face / phone debounce (S8) | Detection sequences on a fake clock | NoFace after 5 s, MultipleFaces after 3 s, LookingAway after 3 s, each once per episode, cleared after 2 s; faces < 0.1 frame height ignored; looking away measured from the student's own baseline (camera beside the screen), a 2 s glance and looking down at the keyboard never raise, the baseline ignores turned-away frames; frames without analysis are no evidence; phone needs two consecutive checks, skipped checks change nothing; status priority | ✅ |
| M5-U-21 | Monitoring loop with the models (S8) | Fake camera returning two faces; dark frames; a model that throws | One MultipleFaces event + violation snapshot (`reason=MultipleFaces`), heartbeat status MultipleFaces; covered frames never analysed (CameraCovered, not NoFace); throwing model absorbed, status Ok | ✅ |
| M5-U-22 | Model files missing / corrupt / shipped (S8) | Empty models folder; text file named `.onnx`; the shipped YuNet + YOLOX-Nano on a grey 640×480, grey-scale and BGRA frame | Checks off, problems listed, `Analyze` → null, no throw; shipped models load and run on the CPU (no faces, phone 0, phone model every other frame) | ✅ |
| M5-U-14 | Evidence storage (server) | Magic bytes; save/read/delete; traversal paths; orphans | JPEG/PNG only (MZ, SVG refused); `{exam}/{session}/…` name + SHA-256; `../`, rooted paths refused; only old unreferenced files removed | ✅ xUnit |
| M5-A-01 | Heartbeat accepted | Student POST `/api/monitoring/heartbeat` | 200 | ✅ |
| M5-A-02 | Heartbeat for foreign session rejected | Other student's session id | Not attributed to that session ✅; status is 200, not 403/404 (`test.fail` until changed) | ⚠️ partial |
| M5-A-03 | Screenshot upload & access (black-box) | Upload jpg / exe; staff list + content; student / outsider read; upload after submit | 200 + listed with sha256, image bytes served; 400; 403 / 403; 409 `SESSION_NOT_ACTIVE` | ✅ Playwright api |
| M5-A-04 | Batch idempotent + timeline (black-box) | Same batch twice; teacher GET timeline; 201 events; foreign session | `{accepted:3}` then `{duplicates:3}`; client time kept, no heartbeats; 400; 404 | ✅ |
| M5-A-05 | Heartbeat ack capture settings | Exam with 45 s interval | Ack `screenshotIntervalSeconds=45`, `requireWebcam=false` | ✅ |
| M5-A-06 | Webcam model findings on the board (S8) | Batch MultipleFaces + `Violation{reason:PhoneDetected}`, heartbeat `cameraStatus=LookingAway`, then "Smiling" | `/live` tile: camera LookingAway, 2 violations, Violation; timeline labels + Suspicious; unknown camera status dropped | ✅ Playwright api |
| M5-I-01 | Screenshot upload | POST multipart jpg | 200, row + file stored | ✅ |
| M5-I-02 | Screenshot size limit | 10 MB upload | 413 | ✅ |
| M5-I-03 | Non-image rejected | Upload .exe renamed .jpg | 400 | ✅ |
| M5-I-04 | Screenshot access control | Teacher of other section GETs screenshots | 403 | ✅ |
| M5-I-05 | Batch events | POST 20 events | 200, 20 stored | ✅ |
| M5-I-06 | ClientEventId dedup | Same batch twice | Stored once | ✅ |
| M5-I-07 | Original timestamps kept | Replay offline events | `RecordedAt` = client time | ✅ |
| M5-I-08 | Disconnected detection | No heartbeat 30 s | Session flagged Disconnected, hub broadcast | ✅ |
| M5-I-09 | Retention cleanup | Screenshot older than RetentionDays | Deleted by job | ✅ |
| M5-I-10 | Evidence content | Upload (violation trigger), staff fetch content | `ScreenshotUploaded` to the exam group; bytes and SHA-256 identical; server-generated path; student 403, unknown id 404 | ✅ |
| M5-I-11 | Upload rules | Foreign session, webcam image for a no-webcam exam, PNG, bad kind, absurd time, after submit | 404, 400 `WEBCAM_NOT_REQUIRED`, 200, 400, stored at server time, 409 | ✅ |
| M5-I-12 | Proctor scoping | Assigned / unassigned invigilator; session of another exam | 200 / 403 on list, content, timeline; 404 | ✅ |
| M5-I-13 | Batch validation & live alerts | >200 events, foreign session, one bad payload; future / 10-min-old / forged "Disconnected" | 400 / 404 / 400 with nothing stored; future clamped + `clockAdjusted`; old kept at client time without a live alert; forged type stored as a non-violation | ✅ |
| M5-I-14 | Session timeline | Heartbeat, focus loss, capture, proctor warning, submit | Events (no heartbeats), capture, warning, Started/Submitted — newest first; `?after=` incremental; outsider and student 403 | ✅ |
| M5-I-15 | Heartbeat liveness + throttling | Same report twice, then a new app | `LastHeartbeatAt` set; one stored row per change (≤ 1/min otherwise); ack has capture settings; broadcast has processes, idle, camera | ✅ |
| M5-I-16 | Watchdog once, reconnect | Sweep twice; finished session; next heartbeat | Flagged once (finished sessions never); `Reconnected` with offline seconds + `StudentReconnected`; tile stays Normal | ✅ |
| M5-I-17 | Retention: missing file + orphans | Row whose file is gone; stray old file | Row removed; orphan deleted | ✅ |
| M5-I-18 | Capture rate limit | `MaxScreenshotsPerMinute=2`, upload 3 | Third → 429 `TOO_MANY_CAPTURES` | ✅ |
| M5-I-19 | Timeline `?after=` with replayed events (S5 verifier) | Cursor taken, then an event recorded 5 min ago arrives in a batch | Returned (filter on server receive time), `at` = client time, `receivedAt` > cursor | ✅ |
| M5-I-20 | Reconnecting heartbeat racing a finalisation (S5 verifier) | Flagged session; force-submit lands between the heartbeat's load and save | Retry keeps the reconnect: `IsDisconnected` false, one `Reconnected` event, status ForceSubmitted | ✅ |
| M5-I-21 | Webcam model findings (S8) | Batch with NoFace, MultipleFaces, `Violation{reason:LookingAway}`, PhoneDetected; heartbeats with each new camera status and "Smiling" | Stored with their own types, 4 live alerts, timeline labels with severity Suspicious; `cameraStatus` broadcast for NoFace / MultipleFaces / LookingAway / Phone, unknown value → null; tile escalates to Violation | ✅ |
| M5-E-01 | Heartbeat shows student online | Simulator heartbeats → live monitor | Tile shows student, Normal | ✅ |
| M5-E-02 | Thumbnail on tile | Simulator uploads screenshot | Tile shows image (blob URL, decoded); a violation capture is noted in the drawer | ✅ |
| M5-E-03 | Disconnected tile | Simulator stops heartbeats | Tile shows "Disconnected" within 40 s; the next heartbeat restores it and the drawer says "Reconnected" | ✅ |
| M5-E-04 | Camera / idle / replay on the tile | Heartbeat with idle 185 s + camera Covered; batch with a fresh CameraCovered and a 10-min-old KeyBlocked | "Idle 3m", "Camera covered"; live label "Camera covered: lens taped"; 2 ALERTS, Suspicious after the next heartbeat | ✅ |
| M5-E-05 | Webcam model findings on the tile (S8) | Heartbeats `cameraStatus` MultipleFaces, NoFace, Ok; batch PhoneDetected | Badge "Multiple faces" → "No face" → gone; live label "Mobile phone detected: …", Suspicious; drawer lists it | ✅ |
| M5-D-01 | Screenshot every interval | 30 s interval, 2 min | ~4 uploads | 📋 |
| M5-D-02 | Network drop & recover | Disable NIC 60 s | Events replayed, none lost | 📋 |
| M5-D-03 | Foreground app reported | Switch to allowed app | Heartbeat shows its process name | 📋 |
| M5-M-01 | Webcam models on a lab PC (S8) | `RequireWebcam` exam: leave the seat 6 s; a second person leans in 4 s; turn to the neighbour 4 s; hold up a phone 3 s; Task Manager CPU | NoFace / MultipleFaces / LookingAway / PhoneDetected each once with a webcam snapshot, tile badges follow; CPU for the app < 15 % on a 4-core PC; readiness fails with nobody / two people in view | 📋 manual |
| M5-P-01 | 30 clients per lab | k6: 30 virtual students heartbeat 10 s + events | p95 < 2 s, 0 errors | 📋 k6 |

## M6 — Submission & Backup

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M6-U-01 | Autosave timer only when dirty | No edits 30 s | 0 saves | ✅ `AutosaveSchedulerTests` (fake clock) |
| M6-U-02 | Autosave fires at 10 s | Edit, wait 10 s | 1 save (not at 9 s; continuous typing → every ~10 s) | ✅ `AutosaveSchedulerTests` |
| M6-U-03 | Local backup round-trip | Write, read | Same answers; file encrypted (AES-256-GCM, DPAPI-wrapped key); atomic write | ✅ `AnswerBackupCodecTests`, `AnswerBackupStoreTests` |
| M6-U-04 | Backup tamper detected | Flip byte (payload, nonce, tag, wrapped key), wrong key, file from another session | GCM tag / DPAPI fails → `Tampered`, backup ignored, `BackupTampered` event reported | ✅ `AnswerBackupCodecTests`, `AnswerBackupStoreTests` |
| M6-U-05 | Submit receipt hash | Same answers | Same SHA-256 as server (identical fixed vectors in both test projects) | ✅ `ReceiptHasherTests` (desktop + backend) |
| M6-U-06 | Offline save queue | 3 saves offline, then online | Replayed in order; newest revision per answer; stops at first offline; 401/5xx/timeout = retry, 400/404 = drop | ✅ `OfflineSaveQueueTests` |
| M6-U-07 | Resume merge | Server answers + local backup | Higher revision wins; unsent local edits replayed; server wins ties | ✅ `AnswerMergeTests` |
| M6-U-08 | Backup housekeeping | Old submitted / abandoned / recent backups | 7 d submitted, 30 d abandoned deleted; resumable = this student's unsubmitted only | ✅ `AnswerBackupStoreTests` |
| M6-U-09 | Resume gate | Session InProgress on the server | "Resume Exam" (no new check-in) until the student's own end, on the server clock | ✅ `StartGateTests` |
| M6-A-01 | Save answer in window | save-answer | 200 | ✅ |
| M6-A-02 | Save after deadline rejected | Exam past end + grace | 400 | ✅ |
| M6-A-03 | Save after submit rejected | Submit then save | 400 | ✅ |
| M6-A-04 | Double submit rejected | Submit twice | 2nd → 400 | ✅ |
| M6-A-05 | Stale revision over HTTP | rev 3, then rev 2, then rev 3 again | 409 `STALE_REVISION` (`currentRevision` 3), replay 200, answer = rev 3 | ✅ |
| M6-A-06 | Receipt + saved answers endpoint | Save, GET answers, submit, GET answers | Answers with revision; submit `{submittedAt, answerCount, sha256}`; same receipt afterwards | ✅ |
| M6-A-07 | Other student's session | GET answers / save into peer's session | 404 / 404 | ✅ |
| M6-A-08 | Foreign question | Save an answer to another exam's question | 400 | ✅ |
| M6-I-01 | Stale revision ignored | Save rev 3 then rev 2 | Answer = rev 3, 409 `STALE_REVISION` | ✅ |
| M6-I-02 | Submit receipt returned | Submit | `{submittedAt, answerCount, sha256}` | ✅ |
| M6-I-03 | Timeout job | Session InProgress 2 min after end | Status TimedOut, graded | ✅ (`SessionTimeoutService`) |
| M6-I-04 | Force-submit-all grades | Teacher force-submit-all | AiGradingResults created | ✅ (since `768df71`) |
| M6-I-05 | Hub force-submit one student grades | Hub `ForceSubmitSession` | Graded | ✅ |
| M6-I-06 | Late submit still locks | Submit after deadline | 200, status Submitted, no new answers | ✅ |
| M6-I-07 | Per-student extra time | ExtraMinutes=10 | Save allowed until end+10 | ✅ |
| M6-I-08 | Answers immutable after submit | Teacher views answers | Unchanged | ✅ |
| M6-I-09 | Restore resumes session | Start, kill client, start again | start-session returns same session | ✅ |
| M6-I-10 | Revision replay rules | Same rev same text / same rev other text / no revision | 200 no-op / 409 / last-write-wins, revision kept | ✅ |
| M6-I-11 | Save validation | Question of another exam; 100 001 chars | 400, nothing stored | ✅ |
| M6-I-12 | Saved answers endpoint | GET own / peer / staff; after submit | 200 with revisions / 404 / 403; receipt equals submit's | ✅ |
| M6-I-13 | Receipt is canonical | Submit two answers | `sha256` = `ReceiptHasher` over stored answers; stored on session; `SessionFinalized` to `exam:{id}` | ✅ |
| M6-I-14 | ExtendStudentTime guarded | Student / other teacher / 0 / 181 min; 10 + 5 | HubException ×4; ExtraMinutes 15, 2× `SESSION_TIME_EXTENDED`, pushed to group + student; heartbeat + `student/current` include it | ✅ |
| M6-I-15 | Sweeper respects extra time | Exam ended 5 min ago, one student +10 min | Only the other session TimedOut (receipt, audit `SESSION_TIMED_OUT`, `SessionFinalized`); the extended one later | ✅ |
| M6-I-16 | After timeout | Save, submit, heartbeat | 400 "time is over", 400, `sessionStatus: TimedOut` | ✅ |
| M6-I-17 | Resume inside extra time | Exam ended 5 min ago, +10 min | `student/start` + `start-session` return the same session; without extra time `EXAM_CLOSED` | ✅ |
| M6-I-18 | Force-submit by session id | Unassigned invigilator; owner; twice | HubException; ForceSubmitted + receipt + audit `SESSION_FORCE_SUBMITTED`; second → HubException; heartbeat reports it | ✅ |
| M6-I-19 | Timed-out sessions in plagiarism | Two students, same theory answer, both timed out by the sweeper | One `PlagiarismResult` for the pair (D-23) | ✅ |
| M6-I-20 | Running session wins over next exam | +10 min, exam ended 5 min ago, another exam tomorrow | `student/current` shows the running exam (`InProgress`, ~5 min left), `student/start` resumes it; after submit the next exam shows (D-24) | ✅ |
| M6-E-01 | Results show submitted student | Simulator submits → teacher results page | Student row with score | ✅ |
| M6-E-02 | Force submit all from UI | Teacher clicks Force Submit All | Simulator's next save → 400 | ✅ |
| M6-E-03 | Per-student force submit button | Drawer → Force submit | Tile shows Submitted | ✅ |
| M6-E-04 | Receipt hash on results page | | Matches simulator receipt | ✅ |
| M6-E-05 | Receipt on the student's result page | Submit, exam ended, student portal | Same hash | ✅ |
| M6-E-06 | Per-student extra time from the drawer | Drawer → Extra Time (10) | Tile "+10 min"; that student's heartbeat clock +10 min | ✅ |
| M6-D-01 | Crash recovery | Kill WPF mid-exam, relaunch | "Resume Exam" + notice; answers restored (unsent ones replayed) | 📋 FlaUI (logic ✅ M6-U-07/08/09) |
| M6-D-02 | Offline answering | Pull LAN, answer 3 Qs, reconnect | "Offline — saved on this PC"; all 3 on server after reconnect | 📋 FlaUI (logic ✅ M6-U-06) |
| M6-D-03 | Auto-submit at 0 | Short exam | Submits automatically; offline → window stays, retries every 10 s | 📋 |
| M6-D-04 | Receipt dialog | Submit | Shows hash + "Verified" (or "Mismatch") | 📋 |

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
| M7-E-04 | Invigilator UI route | Login as invigilator | Lands on `/invigilator/live-monitor`; proctor badge, no teacher nav / create-exam; teacher & admin URLs bounce back | ✅ |
| M7-E-05 | Departments & courses page | Admin → Departments & Courses: add dept + course, delete non-empty dept, rename + delete course, delete dept | Rows/counts update; 409 message shown | ✅ |
| M7-E-06 | Admin roles only for SuperAdmin | Add-user role list as Admin vs SuperAdmin | Admin: Student/Teacher/Invigilator, admin rows locked; SuperAdmin: + Administrator, Super Administrator | ✅ |
| M7-E-07 | Teacher assigns an invigilator | Eligibility page → Invigilators → Assign / Remove | Chip appears; invigilator's exam list = that exam; removal empties it | ✅ |
| M7-E-08 | Unassigned teacher gets no live data | Other section's teacher opens Live Monitor on a running exam; then is assigned as co-proctor | "not assigned to supervise" banner, no tiles; after assignment tiles appear | ✅ |
| M7-E-09 | Unassign stops an open live monitor | Invigilator watching a tile; teacher unassigns them | "access … withdrawn" banner, board cleared, later heartbeats never reach the page | ✅ |
| M7-A-05 | Only a SuperAdmin creates admins | Admin POST role Admin; SuperAdmin POST; Admin deactivates new admin | 403 `SUPERADMIN_REQUIRED`; 201; 403 (SuperAdmin 200) | ✅ |
| M7-A-06 | Departments & courses CRUD (HTTP) | Create/dup/list/update/delete | 201/409/filtered list/200/204; non-empty dept 409; teacher write 403 | ✅ |
| M7-A-07 | Proctor assignment scopes invigilator | Assign to exam A (repeat), invigilator lists/opens, unassign | 201 then 200; list = [A]; other exam 404; no answer key; after unassign [] | ✅ |
| M7-A-08 | Demoting an admin ends their sessions | SuperAdmin creates Admin, Admin signs in, SuperAdmin demotes to Teacher | Old token 401; re-login role Teacher, admin API 403 | ✅ |
| M7-I-01 | CSV import | POST import 3 rows | 3 created, bad rows reported | ✅ |
| M7-I-02 | Departments CRUD | POST/GET/PUT/DELETE departments | Works | ✅ |
| M7-I-03 | Courses CRUD | Same for courses | Works | ✅ |
| M7-I-04 | SuperAdmin manages admins | SuperAdmin creates Admin | 201; Admin cannot create SuperAdmin | ✅ |
| M7-I-05 | Invigilator sees only assigned exams | Invigilator GET exams | Only ExamProctors rows | ✅ |
| M7-I-06 | Proctor assignment contract | POST twice, GET, bad targets, other teacher/student/invigilator, admin DELETE | 201 → 200; `INVALID_PROCTOR_ROLE` / `ALREADY_OWNER` 400; 404s; 403s; 204 then 404; one `PROCTOR_ASSIGNED` + one `PROCTOR_UNASSIGNED` | ✅ |
| M7-I-07 | Invigilator scoping end-to-end | Before/after assignment and after unassignment | List/detail/readiness 404/403 → 200 (no reference answer, MCQ key or hidden test case) → revoked incl. hub join | ✅ |
| M7-I-08 | Hub group = owner, admin, assigned | Other-section teacher, admin, unknown exam, co-proctor teacher | HubException; joined; HubException; joined after assignment | ✅ |
| M7-I-09 | Invigilator warns / force-submits only supervised exams | Hub `SendWarning`, `ForceSubmitSession` before/after assignment | HubException + session untouched; then warning stored + `ForceSubmitted` | ✅ |
| M7-I-10 | Privileged accounts need a SuperAdmin | Admin creates/edits/demotes/deactivates/signs out/deletes an Admin or SuperAdmin, promotes teacher; CSV row role Admin | 403 ×7 (`PRIVILEGED_ACCOUNT_DENIED` audited), import row skipped; own profile 200, own role 400; SuperAdmin 200 | ✅ |
| M7-I-11 | Department validation | Blank, 129 chars, case-insensitive dup, rename to dup, unknown id, roles | 400/400/409/409/404; invigilator GET 200, student 403, teacher DELETE 403; audit `DEPARTMENT_CREATED` | ✅ |
| M7-I-12 | Course validation | Code normalised, dup code (create/rename), unknown dept, blanks, same code other dept, delete with sections | `CS505`; 409; `UNKNOWN_DEPARTMENT`; 400; 201; `COURSE_HAS_SECTIONS` 409 | ✅ |
| M7-U-01 | SuperAdmin bootstrap | `SuperAdminBootstrap.EnsureAsync` with no config / short password / new email / existing admin / existing SuperAdmin | NotConfigured / InvalidPassword / Created (audited) / Promoted (password kept, reactivated) / AlreadyPresent | ✅ |
| M7-U-02 | Demo seed roles + course FK | `DbSeeder.SeedAsync` | One SuperAdmin; invigilator proctors the open demo exam; course `DeptId` = its department | ✅ |
| M7-U-03 | Live group revocation bookkeeping | `ExamGroupTracker` joins/leaves/disconnects, revoke one exam vs all | Only that user's connections in that group removed + notified; left/disconnected ones never revoked | ✅ |
| M7-I-13 | Unassign removes open live connection | Invigilator joined exams A and B; teacher unassigns A; hub disconnect | Removed from `exam:A` only, `ExamAccessRevoked` to that connection; B kept; disconnect forgets it | ✅ |
| M7-I-14 | Role change / deactivation take effect now | SuperAdmin demotes Admin → Teacher; name-only edit; Admin deactivates a joined invigilator | Old token 401, `previousRole` audited, re-login is Teacher (403 on admin API); rename keeps session; invigilator dropped from the group | ✅ |
| M7-I-15 | Exam list `canSupervise` | Owner, other-section teacher (before/after co-proctor assignment), admin | true / false → true / all true | ✅ |

## M8 — Exam Creation & Configuration

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M8-A-01 | Create exam via API | Teacher POST exam with MCQ+coding | 201 | ✅ |
| M8-A-02 | Invalid MCQ index | correctOptionIndex out of range | 400 | ✅ |
| M8-A-03 | Coding without test cases | | 400 | ✅ |
| M8-E-01 | Create exam wizard end-to-end | UI: details → 2 questions → publish | Exam in dashboard list | ✅ |
| M8-I-01 | Capture intervals | Create/edit with screenshot/webcam interval; 9 s / 601 s | Default 60; persisted on create + edit; in `/student/current`; out of range → 400 | ✅ xUnit (`Module8CaptureSettingsTests`) |
| M8-E-02 | Monitoring settings step | Set screenshot interval 5 s, then 30 s (webcam 120 s) | 5 s rejected in the wizard; 30/120 persisted; edit page shows them | ✅ |
| M8-E-03 | Allowed apps picker | Pick Visual Studio Code + Code::Blocks from the catalogue, add `sublime_text`, remove one chip | Chips show exe names; picked apps leave the list; `AllowedApps` = `["code.exe","sublime_text.exe"]` | ✅ |

## M9 — Eligibility & Access Control

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M9-U-01 | Attendance % calc | 30/40 | 75% | ✅ (via API) |
| M9-U-02 | Threshold boundary | 74.9% vs 75% min | ineligible / eligible | ✅ (via API) |
| M9-U-03 | Zero classes | total=0 | Not computed, flagged | ✅ (via API) |
| M9-U-04 | Attendance CSV parser | Header in any order, BOM/CRLF/quotes/`;`, bad rows, duplicates, missing header | Rows parsed by header name; each bad row carries a reason | ✅ |
| M9-U-05 | Eligibility rule | 749/1000, 750/1000, 24/40, 2/3, no record, 0 classes | Unrounded compare; notes "Attendance 60% < 75%" / "… ≥ …"; not computed | ✅ |
| M9-U-06 | Seat matching | MachineName / Ip / Either; HostName vs seat label; IPv4-mapped IPv6 | Matches as specified | ✅ |
| M9-A-01 | Manual eligibility toggle | Teacher sets ineligible | Student start → 400 | ✅ |
| M9-A-02 | Attendance import + recalculate (black-box) | CSV `attended,email,total` + unknown email; recalculate | 75% eligible / 60% ineligible with note; unknown email reported; student upload 403 | ✅ |
| M9-A-03 | Seat enforcement, desktop path | Seat R1C2, simulator on another PC; then from R1C2 | 403 `SEAT_MISMATCH` + audit + `seatStatus=Mismatch`; then 200 | ✅ |
| M9-A-04 | Roster owner-only (black-box) | Second teacher calls assignments / eligibility / seat / unassign | 403 each; owner still sees the student eligible | ✅ |
| M9-I-01 | Attendance CSV import | POST CSV 2 rows | Records stored | ✅ |
| M9-I-02 | Recalculate sets ineligible | 60% attendance, min 75 | IsEligible=false, note "Attendance 60% < 75%" | ✅ |
| M9-I-03 | Recalculate keeps overrides | Manual override true, attendance 60% | Stays eligible | ✅ |
| M9-I-04 | Decisions audited | Recalculate | `ELIGIBILITY_COMPUTED` per student | ✅ |
| M9-I-05 | Seat match by machine name | Assigned PC-01, desktop login from PC-01 | start 200 | ✅ |
| M9-I-06 | Seat mismatch blocked | Assigned PC-01, logs in from PC-02 | 403 `SEAT_MISMATCH`, audit, live alert, no session | ✅ |
| M9-I-07 | Seat match by IP mode | SeatMatch=Ip | Compare request IP | ✅ |
| M9-I-08 | No seat assigned → not enforced | WorkstationId null | 200 | ✅ |
| M9-I-09 | Heartbeat from wrong seat flagged | Mid-exam seat change | Violation `SeatMismatch` | ✅ |
| M9-I-10 | Auto-register off by default | Student login from new PC | No workstation auto-created | ✅ |
| M9-I-11 | Import authorisation + validation | Other teacher's section; bad header; non-enrolled student; re-import | 403; 400; imported with warning; updated in place; `ATTENDANCE_IMPORTED` audited | ✅ |
| M9-I-12 | Recalculate guards | Other teacher / student / ended exam | 403 / 403 / 409 | ✅ |
| M9-I-13 | Only changed rows become overrides; clear override | Save 2 rows (1 changed); clear override; recalculate | 1 `ELIGIBILITY_OVERRIDDEN`; `ELIGIBILITY_OVERRIDE_CLEARED`; attendance applies again | ✅ |
| M9-I-14 | Heartbeat mismatch de-duplicated | 3 heartbeats from wrong seat | 1 `SeatMismatch` event, 1 audit; heartbeat `seatStatus=Mismatch`, tile Violation | ✅ |
| M9-I-15 | student/start + student/current honour the seat | Desktop login from wrong / right PC | `seatStatus=Mismatch`, `canStartNow=false`, 403; then Ok, 200 | ✅ |
| M9-I-16 | Workstation edit/delete | Set HostName; delete assigned / spare seat | HostName drives matching; 409 / 200; teacher 403 | ✅ |
| M9-I-17 | Auto-register opt-in + unregistered list | Default vs `Labs:AutoRegister=true` | PC listed at `/api/labs/unregistered-machines` / workstation created | ✅ |
| M9-I-18 | EnforceSeat off; desktop client required | `enforceSeat=false`; `Exams:RequireDesktopClientForStart=true` | Start from any PC 200; web token 403 `DESKTOP_CLIENT_REQUIRED` | ✅ |
| M9-I-19 | Exam roster is owner-only | Other teacher: GET assignments, PUT eligibility, add/remove student, seat mapping; owner/admin; unknown exam | 403 ×5, nothing changed or audited; 200; 404 | ✅ |
| M9-I-20 | Ip mode checks web tokens too | `SeatMatch=Ip`, web-portal token from wrong / right IP; MachineName mode | 403 `SEAT_MISMATCH` + audit / 200; `seatStatus=NotChecked` | ✅ |
| M9-E-01 | Eligibility page loads | Teacher → Eligibility | Assignments table | ✅ |
| M9-E-02 | Toggle eligibility in UI | Click toggle | Simulator start → 400 | ✅ |
| M9-E-03 | Attendance upload dialog | Upload CSV | Attendance column filled | ✅ |
| M9-E-04 | Recalculate button | Click | Ineligible rows show reason | ✅ |
| M9-E-05 | Seat mismatch alert | Simulator starts at R1C1, re-logs in from R1C2 | Tile "Seat mismatch: logged in from R1C2 (assigned R1C1)", Critical | ✅ |
| M9-E-06 | Manual override badge | Override 1 row, recalculate, clear override | "Manual" badge kept through recalculation; cleared → attendance applies | ✅ |
| M9-M-01 | Seat enforcement on real lab PCs | Register each PC's Windows name as HostName; sit at the wrong PC | Start refused with seat message; proctor alerted | 📋 |

## M10 — Live Proctoring Dashboard

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M10-U-01 | Warning inbox: repeated ack | Same warning in 3 heartbeat acks | Raised once, stays current | ✅ desktop xUnit (S6) |
| M10-U-02 | Warning inbox: order and acknowledge | Two warnings, acknowledge each | Oldest first; acknowledged ones leave the banner | ✅ (S6) |
| M10-U-03 | Warning inbox: acknowledged offline | Acknowledged, server still resends it (mark-read failed) | Not shown again | ✅ (S6) |
| M10-U-04 | Warning inbox: malformed entries | Null list, empty id, blank body | Ignored | ✅ (S6) |
| M10-U-05 | Unlock deadline on a skewed PC clock | Server says until +5 min, PC clock 7 min behind | Local deadline = PC now + 5 min | ✅ (S6) |
| M10-U-06 | Unlock deadline limits | Null / past / now / +5 h | Null, null, null, capped at 30 min | ✅ (S6) |
| M10-U-07 | Suspend decisions | Locked→unlocked, jitter, new unlock, relock, expiry, re-unlock | Suspend / None / Reschedule / Resume / Resume / Suspend | ✅ (S6) |
| M10-I-01 | Incident create | POST incident | 201, audit | ✅ (S6) |
| M10-I-02 | Incident escalate/resolve | PATCH status | Transition stored, invalid transitions 400 | ✅ (S6) |
| M10-I-03 | Incident access control | Teacher of other section | 403 | ✅ (S6) |
| M10-I-04 | Unlock session | Hub UnlockSession 5 min | Event sent to student, audited | ✅ (S6) |
| M10-I-05 | Extend single student | ExtendStudentTime | Only that session's end moves | ✅ (Stage S4) |
| M10-I-06 | Hub group isolation | Student joins exam group | HubException | ✅ (existing) |
| M10-I-07 | Teacher can't force-submit other teacher's exam | Hub call | HubException | ✅ (existing) |
| M10-I-08 | Proctor assignment | Add ExamProctor | Invigilator can join group | ✅ (Stage S3, built with M7) |
| M10-I-09 | Warning reaches the student (D-05) | Hub SendWarning; heartbeats; student marks it read | `ProctorWarning` to `user:{id}` + `ProctorWarningSent` to the exam group; audit `PROCTOR_WARNING_SENT`; ack `warnings` repeats it until read, then empty; audit `PROCTOR_WARNING_ACKNOWLEDGED`; other section's teacher / blank text → HubException | ✅ (S6) |
| M10-I-10 | Unlock in the heartbeat ack, relock, limits | Unlock 31 / 0 min / blank reason / other teacher; unlock 5; relock; unlock after submit | HubException ×4; ack `unlockedUntilUtc` ≈ now+5 min; `SessionUnlocked` to the group; relock → null + `SESSION_RELOCKED` + `SessionRelocked` to the student; finished session refused | ✅ (S6) |
| M10-I-11 | Board snapshot | Two started, one heartbeats + violation + capture + incident; GET `/exams/{id}/live` | Only the heartbeating student, with violations, tile status, last violation, apps, latest capture, open incidents; other teacher / student 403 | ✅ (S6) |
| M10-I-12 | Incidents: list, broadcast, proctors, validation | Create; assigned / unassigned invigilator; unknown session; bad severity; empty / 2001-char notes; notes-only edit; resolve; list | `IncidentChanged` to the exam group; 201 / 403; 403; 400 `INVALID_SEVERITY`; 400 ×2; status kept + `INCIDENT_UPDATED`; `statusNote`, `resolvedByName`; `?status=Open` filters | ✅ (S6) |
| M10-I-13 | Apps seen during the session | Heartbeats "SmartExam,chrome" then "SmartExam,code"; GET processes | `current` = code, SmartExam; `seen` keeps chrome (not running); other teacher 403 | ✅ (S6) |
| M10-I-14 | Timeline shows incidents and proctor actions | Incident + unlock; GET session timeline | `Incident` item with the notes; `Action` item `SESSION_UNLOCKED` with the reason | ✅ (S6) |
| M10-I-15 | Whole-exam actions are owner/admin only (S6 verifier) | Other-section teacher and a co-proctoring teacher: extend-time, force-submit-all; other teacher: PUT / DELETE a scheduled exam, POST an exam into the section; then the owner | 403 ×7, duration and status unchanged, no `EXAM_TIME_EXTENDED`; the co-proctor can still give one student extra time; owner 200 ×4 | ✅ (S6 verifier) |
| M10-I-16 | Other teachers see no answer key | Unrelated teacher GET `/api/exams/{id}` | Reference answer, MCQ key, expected output null; the owner still gets them | ✅ (S6 verifier) |
| M10-I-17 | Mark-all-read of a warning is audited | Warning sent; student `PATCH /api/notifications/read-all` | Ack `warnings` empty; one `PROCTOR_WARNING_ACKNOWLEDGED` | ✅ (S6 verifier) |
| M10-A-01 | Incident lifecycle over HTTP | Create, escalate, resolve, reopen, bad values, other teacher, student | 201 / 200 / 200 / 400 `INVALID_TRANSITION` / 400 / 403 / 403; one audit row per step | ✅ (S6) |
| M10-A-02 | Board snapshot over HTTP | Heartbeat (apps, idle, camera) + violation + capture + incident | One tile with all of it; a started-but-silent student absent; other teacher 403 | ✅ (S6) |
| M10-A-03 | Apps seen during the session | Two heartbeats with different apps | `current` latest, `seen` keeps the closed app | ✅ (S6) |
| M10-A-04 | Whole exam is owner/admin only over HTTP | Other teacher: extend-time, force-submit-all, DELETE, GET detail; owner: extend, force-end | 403 ×3; no answer key; exam unchanged; owner 200 ×2 | ✅ (S6 verifier) |
| M10-E-01 | Live monitor loads | Teacher → Live Monitor | Page renders, no error | ✅ |
| M10-E-02 | Student tile appears on heartbeat | Simulator starts (both `start-session` and the desktop `student/start` path) + heartbeats | Tile with student name | ✅ (desktop-path bug found & fixed) |
| M10-E-03 | Status progression | KeyBlocked → UnauthorizedProcess → ProcessKilled | Stable → Warning → Suspicious → Critical | ✅ |
| M10-E-04 | Send warning | Teacher sends warning to tile | Simulator's notifications contain it | ✅ |
| M10-E-05 | Extend time | Teacher extends 10 min | Simulator `remainingSeconds` grows ~600 | ✅ |
| M10-E-06 | Detail drawer | Click tile | Timeline + process list (apps seen earlier in the session too); server timeline survives a reload | ✅ (S6) |
| M10-E-07 | Mark incident | Drawer → Mark incident | Incident badge on tile; resolving with a note clears it | ✅ (S6) |
| M10-E-08 | Screenshot carousel | Simulator uploads a capture (the pending test never uploaded one — fixed), drawer | One rendered image; a violation capture arriving while open joins the strip | ✅ (S6) |
| M10-E-09 | Unlock button | Drawer → Unlock 5 min | Toast + audit row; ack `unlockedUntilUtc`; "Unlocked" tile badge; "Lock again" ends it | ✅ (S6) |
| M10-E-10 | Invigilator restricted view | Invigilator → other exam | Not listed | ✅ (also: tiles appear, no extend/end buttons, `?examId=` of another exam ignored) |
| M10-E-11 | Exam picker | Teacher with two running exams switches exam | Header + `?examId=` follow the picker; board shows only the selected exam's students | ✅ |
| M10-E-12 | Default exam is a supervisable one | Teacher co-proctors one of two running exams, opens Live Monitor without `?examId=` | Opens the assigned exam (tiles, no denial banner); list flags `canSupervise` false/true | ✅ |
| M10-E-13 | Warning reaches the desktop client (D-05) | Drawer → send warning; simulator heartbeats, acknowledges | "Warning Message Sent"; ack `warnings` carries it every beat until marked read; drawer timeline shows it | ✅ (S6) |
| M10-E-14 | Board survives a reload; readiness on the tile | Readiness passed, violation, capture; reload without a new heartbeat | Tile rebuilt from `GET /exams/{id}/live` (1 alert, Warning, thumbnail, "Ready"); drawer shows the readiness items | ✅ (S6) |
| M10-D-01 | Warning banner on the exam window | Lab PC: proctor sends a warning during an exam | Amber banner within one heartbeat (≤ 10 s); Acknowledge removes it and marks it read | 📋 (lab PC only) |
| M10-D-02 | Manual unlock on the exam window | Lab PC (Enforce mode): Unlock 5 min, use Alt+Tab / Task Manager, wait | Guards off and blue notice within one heartbeat; window may go behind; guards back on at the deadline (or on "Lock again"); `LockdownSuspended` / `LockdownResumed` on the timeline | 📋 (lab PC only) |

## M11 — AI-Assisted Evaluation

Built in S7. Coding answers are compiled and run in a background queue: the Docker sandbox (`gcc:13`, no network, 256 MB, 0.5 CPU, 64 PIDs, read-only root, `nobody`) when Docker is up, else the local MinGW runner (Windows Job Object; labelled "not isolated"); both use `-std=c++17 -O0`. Theory answers are graded offline (TF-IDF + key terms). Plagiarism is token-normalised 5-gram winnowing. The LLM grader/summary is off by default. Tests use `Grading:Runner=Fake` unless they say otherwise; Docker / MinGW tests skip themselves where the runner is missing.

| ID | Title | Steps | Expected | Status |
|----|-------|-------|----------|--------|
| M11-U-01 | MCQ correct | index matches | full marks | ✅ (via xUnit flow) |
| M11-U-02 | MCQ wrong | | 0 | ✅ |
| M11-U-03 | Test-case scoring weights | 2/3 passed, weights 1,1,2 | 50% | ✅ xUnit (`TestCaseScorerTests`) |
| M11-U-04 | Compile error → 0 with message | | 0, High, compiler output in justification | ✅ xUnit |
| M11-U-05 | Timeout counts as fail | infinite loop | that case fails, others run | ✅ xUnit (scorer + real Docker / MinGW runner tests) |
| M11-U-06 | Output comparison trims whitespace | "5\n" vs "5" | pass | ✅ xUnit (`OutputComparerTests`) |
| M11-U-07 | TF-IDF cosine identical text | | ≈1.0 | ✅ xUnit (`TheoryGraderTests`) |
| M11-U-08 | Plagiarism ignores identifier renames | Same code, renamed vars | similarity ≥ 90% | ✅ (API + `PlagiarismEngineTests`) |
| M11-U-09 | Plagiarism ignores comments/whitespace | | ≥ 95% | ✅ |
| M11-U-10 | Different solutions low similarity | Two distinct algorithms | < 30% | ✅ |
| M11-U-11 | Output tolerance rules | CRLF, trailing spaces, ignore-case, numeric tolerance | Lenient only where the case allows it | ✅ xUnit (`OutputComparerTests`) |
| M11-U-12 | Hidden cases never quoted | Hidden case fails | Justification says "Hidden case #2: failed", no input/expected/actual | ✅ xUnit |
| M11-U-13 | Near-correct and rounding | 4/5 pass; 1/3 of 10 marks | Near-correct flagged; marks round to 0.5 | ✅ xUnit |
| M11-U-14 | Static analysis signals | Nested / sequential / do-while loops, recursion, Python, lookup tables, many ifs | Loop count + nesting → O(1)/O(n)/O(n^2)/O(n^3+); recursion found; hard-coding suspicion Low/Medium/High | ✅ xUnit (`CodeAnalyzerTests`) |
| M11-U-15 | Theory grader variants | Paraphrase, partial, unrelated, rubric phrases, keywords only, no model answer | Marks, confidence and missing terms as specified; no model answer → 0, Low | ✅ xUnit |
| M11-U-16 | LLM features off by default and safe | No `Enabled`, no key; fenced / trailing-comma / truncated JSON; HTTP 500; 429 | Never calls the network when off; robust parse; null fallback; retried on 429; Gemini key in a header; no hidden outputs sent | ✅ xUnit (`LlmGradingTests`, fake HTTP handler) |
| M11-U-17 | Docker harness and parser | Build harness, parse COMPILED / CASE lines | `-std=c++17 -O0`, per-case limits, LF only; 124 = timeout, cgroup OOM = memory | ✅ xUnit |
| M11-U-18 | Real Docker runner | gcc:13: sum program, endless loop + next case, compile error | Correct outputs; only the looping case times out; compile error is a report | ✅ xUnit (skips without Docker) |
| M11-U-19 | Real local runner (Job Object) | MinGW: sum, endless loop, 1 GB malloc, `system("cmd")` + server env var, compile error | Outputs right; timeout per case; memory limit reported; child process blocked and environment scrubbed | ✅ xUnit (skips without MinGW / Windows) |
| M11-I-01 | Submit returns before grading finishes | Slow fake runner | Submit 200 < 1 s, status Pending | ✅ |
| M11-I-02 | Worker grades pending answers | Run worker | Status Graded with per-case results | ✅ |
| M11-I-03 | Hidden test results not shown to student | Student result | Only visible cases detailed | ✅ |
| M11-I-04 | Sandbox has no network | Code that opens socket | Fails, no egress | ✅ (real Docker; skips without it) |
| M11-I-05 | Sandbox memory limit | Allocates 1 GB | Killed, case fails, "memory" in justification | ✅ (real Docker; skips without it) |
| M11-I-06 | Plagiarism matrix | 3 students | 3×3 symmetric matrix | ✅ |
| M11-I-07 | Matched segments line ranges | | Ranges present | ✅ |
| M11-I-08 | Teacher override wins | Override after AI grade | Final mark = override | ✅ |
| M11-I-09 | Grades are the owner's (D-34) | Other teacher / student: results, plagiarism, matrix, override, accept-ai, regrade | 403 each; owner + admin 200; unknown exam 404 | ✅ |
| M11-I-10 | Matrix rules | Practice exam; unknown / MCQ question filter; per-question matrix | 400; 400; per-question values, diagonal 100 | ✅ |
| M11-I-11 | Accept AI suggestions | Owner POST `/grading/accept-ai` (coding with test cases + theory with a model answer) | Overrides = suggestions for Graded answers without one; audit `AI_SUGGESTIONS_ACCEPTED`; second call accepts 0 | ✅ (S7 verifier: own exam, see M11-I-22) |
| M11-I-12 | Regrade | POST `/api/answers/{id}/regrade` | Pending, then Graded again; `QueueVersion` 1; audit `AI_REGRADE_REQUESTED` | ✅ |
| M11-I-13 | Regrade refused | Running session / MCQ / unknown answer | 409 / 400 / 404 | ✅ |
| M11-I-14 | Compile error through the API | `#compile-error` (fake) | 0, High, compiler output, cases NotRun, analysis present; student sees 1 visible case + `hiddenTotal` 2, no analysis | ✅ |
| M11-I-15 | GradingCompleted broadcast | Submit coding answer | `GradingCompleted` to `exam:{id}` with session + status | ✅ |
| M11-I-16 | Restart-safe queue | Pending row with nothing queued; start a worker | Row re-queued and graded | ✅ |
| M11-I-17 | Override validated and audited | Marks 11 / −1 on a 10-mark question; 6.5 | 400, 400; 200 + audit `GRADE_OVERRIDDEN` | ✅ |
| M11-I-18 | Test-case settings | Weight 0, limit 50 ms, tolerance −0.1, language java; valid settings | 400 each; stored and returned to staff; hidden cases not sent to students | ✅ |
| M11-I-19 | Weighted cases + TF-IDF via API | Weights 3 / 1; theory with model answer | 20/20 with weights reported; engine TF-IDF, 10/10 | ✅ |
| M11-I-20 | Runner None | `Grading:Runner=None` | Coding graded at once: Low, "Not compiled … teacher review required" | ✅ |
| M11-I-21 | Engine failure | Docker CLI missing, 1 attempt | Status Failed, 0, Low, cases NotRun | ✅ |
| M11-I-22 | Bulk accept skips non-grades (D-36) | Runner None: coding "Not compiled" + theory with model answer | accepted 1 (theory), `needsManualMark` 1, no override on the coding answer | ✅ (S7 verifier) |
| M11-I-23 | LLM reply of an unexpected shape (D-37) | LLM on, provider returns `content: 42` | Falls back to the offline TF-IDF grade (Graded), never stuck Pending | ✅ (S7 verifier) |
| M11-I-24 | Engine label fits its column (D-37) | LLM on with a 124-character model name | Graded; engine starts "LLM (" and is ≤ 64 characters | ✅ (S7 verifier) |
| M11-I-25 | Unexpected failure ends as Failed (D-37) | The database refuses the first save of the grade | Status Failed, 0, "internal error … teacher review required", cases NotRun, `GradingCompleted` Failed | ✅ (S7 verifier) |
| M11-A-01 | Coding answer really compiled, labelled with engine | Submit coding answer that does not compile | Submit returns at once; then Graded: 0, High, "compilation failed", engine Docker/MinGW, compiler output, 2 case rows | ✅ (rewritten in S7: was the keyword heuristic) |
| M11-A-02 | Plagiarism pair flagged | Two simulators submit identical theory answers | Pair in plagiarism list | ✅ |
| M11-A-03 | Correct code passes all cases | Real runner, visible + hidden case; student result after the exam | 20/20, both Passed, analysis O(n); student sees 1 case + hidden tally, never the hidden input | ✅ |
| M11-A-04 | Matrix over HTTP | 3 students; other teacher; practice exam | Symmetric, identical pair 100, different < 30; 403; 400 | ✅ |
| M11-A-05 | Regrade + accept-ai over HTTP | Regrade theory answer, then accept | Pending → same mark; override = suggestion; both audited | ✅ |
| M11-E-01 | Results page shows AI suggestion | Teacher → results, non-compiling code | "Compilation failed", High Confidence, 0 / 20, compiler output, case rows "Not run" | ✅ (updated in S7) |
| M11-E-02 | Override in UI | Edit marks | 10/10 from TF-IDF; saved, shown as Overridden; AI suggestion kept | ✅ (updated in S7) |
| M11-E-03 | Plagiarism heat map | Analytics page | 3×3 heat map, identical pair 100 | ✅ |
| M11-E-04 | Test-case breakdown | Results drawer | Per-case pass/fail incl. hidden marker, 20 / 20, complexity O(n) | ✅ |
| M11-E-05 | Side-by-side plagiarism | Two students, renamed copy | Flag 100%, matched lines highlighted, 2×2 heat map in the tab | ✅ |
| M11-E-06 | Accept AI suggestions (UI) | Button + confirm | Toast "Accepted 2 suggestion(s)", both answers Overridden | ✅ |
| M11-E-07 | Editing keeps test-case settings (D-38) | Exam created with category / ignore-case / tolerance, saved from the edit page | Weight, category, ignoreCase, floatTolerance unchanged | ✅ (S7 verifier) |

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
| D-04 | M10-E-* | Live Monitor shows only the first Active exam; no exam picker | **Fixed** (S3) — exam picker (`?examId=`), invigilators only get assigned exams (M10-E-10/11/12) |
| D-05 | M10-E-04 | Hub `SendWarning` only stores a notification — nothing is pushed live to the student client | **Fixed** (S6) — pushed as `ProctorWarning` to the student's connections and carried in every heartbeat ack until acknowledged; the exam window shows a banner with Acknowledge (M10-I-09, M10-E-13) |
| D-06 | M5-A-02 | Heartbeat naming another student's session returns 200 (ignored, not attributed) instead of 403/404 | Open (low) |
| D-07 | review | Live Monitor logged "Window Focus Lost" whenever the window title wasn't "Exam Browser" | **Fixed** — uses the client's foreground flag |
| D-08 | M3-I-01 | `start-session` never checked `StartTime` — a student could start a future exam; the desktop Start button enabled early (it only looked at time-to-end) | **Fixed** — `EXAM_NOT_STARTED` on both start paths + `StartGate` on the server clock |
| D-09 | M9-I-10 | Every student login auto-registered its PC into "Systems Engineering Lab", defeating seat enforcement | **Fixed** — only with `Labs:AutoRegister=true` (dotnet-run demo profiles) |
| D-10 | M9-I-19 | Any teacher could read another section's attendance (via `GET /exams/{id}/assignments`) and change another teacher's eligibility, roster and seat map | **Fixed** — owner-teacher / admin guard on all Module 9 roster endpoints |
| D-11 | M9-I-20 | `SeatMatch=Ip` skipped web-portal tokens, so a student could start from a wrong PC through the browser | **Fixed** — web tokens are checked by the server-observed request IP in Ip / Either mode |
| D-12 | M1-E-05 | Web panel logout only cleared localStorage; the access token stayed valid server-side until it expired | **Fixed** — logout calls `POST /api/auth/logout`, which also revokes the refresh-token family |
| D-13 | M1-E-07 | Web logout after an idle spell was sent with the expired access token (proactive refresh skipped for `/auth/logout`) → 401, and the refresh-token family stayed valid for up to 12 h | **Fixed** — logout is refreshed proactively; refresh calls time out after 10 s |
| D-14 | M1-I-13 | Password change / admin password reset left every other refresh token (12 h) and access token alive; logout left the family's earlier access tokens alive | **Fixed** — `RefreshTokenService.RevokeOtherSessionsAsync`, logout revokes the family's sessions |
| D-15 | M4-U-18 | `LockdownCoordinator.Stop()` restored the Task Manager policy last; from the crash / ProcessExit handlers (non-UI thread) the clipboard step can throw first, leaving `DisableTaskMgr=1` until the next app start | **Fixed** — `Core/Lockdown/LockdownTeardown` runs isolated steps, Task Manager restore first |
| D-16 | review (S4) | The exam window's own countdown ignored teacher extensions (only the dashboard resynced from the heartbeat) and auto-submitted at the original end | **Fixed** — `ExamWindow.ResyncRemaining` from every heartbeat ack (exam extension + per-student extra time) |
| D-17 | M6-I-18 | After a proctor force-submit the exam window stayed open and locked (saves 400, Submit "not active") until the timer ran out | **Fixed** — heartbeat ack carries `sessionStatus`; the window closes with a message |
| D-18 | M6-U-09 | After a crash and relaunch the Start button was disabled for the `InProgress` session — no resume path in the UI | **Fixed** — "Resume Exam" (`StartGate.CanResume`) + backup notice |
| D-19 | review (S4) | A failed auto-submit at time-out (offline) still closed the window; answers not yet saved were lost | **Fixed** — submit flushes the offline queue first; offline → window stays, answers kept encrypted on disk, retry every 10 s |
| D-20 | review (S4) | Hub `ForceSubmitSession(studentId)` picks the student's first in-progress session — ambiguous with a practice session open | **Fixed** — `ForceSubmitSessionById(sessionId)`, used by the Live Monitor |
| D-21 | M6-I-11 | `save-answer` accepted any question id, so a student could store answers to another exam's questions in their session | **Fixed** — question must belong to the session's exam; text ≤ 100 000 chars |
| D-22 | review (S4) | The desktop app only saved answered questions, so clearing an answer never reached the server | **Fixed** — revisioned saves send every edit, including a cleared answer |
| D-23 | M6-I-19 (S4 verifier) | Plagiarism detection only compared against `Submitted`/`ForceSubmitted` sessions, so sessions the new sweeper finalised as `TimedOut` were never compared | **Fixed** — every finalised session (`Status != InProgress`) is compared |
| D-24 | M6-I-20 (S4 verifier) | `student/current` and `student/start` chose the next scheduled exam once the running exam's own window had closed, so a student in extra time got no "Resume Exam" and the resume was refused | **Fixed** — both prefer the assignment whose session is still running inside the student's own deadline |
| D-25 | M5-I-13 (S5) | A client could post the server-only event types `SeatMismatch`, `Disconnected`, `Reconnected` (and `Heartbeat` through "Violation"+reason) and so forge or bury server findings | **Fixed** — `ViolationClassifier.ServerOnlyTypes` are resolved like unknown names |
| D-26 | review (S5) | Lockdown violations were sent fire-and-forget: lost whenever the network dropped | **Fixed** — `LockdownCoordinator` reports into `LiveMonitoringService.ReportViolation` (encrypted on-disk outbox, batched, replayed in order) |
| D-27 | M5-U-15/16 (S5 verifier) | The webcam monitoring thread only caught OpenCV/DLL exceptions; any other camera failure, or a webcam snapshot the spool could not write (disk full), was unhandled on a raw thread and ended the exam app mid-exam | **Fixed** — every loop iteration and subscriber call is isolated (`IWebcamFrameSource` makes it testable); `AddWebcamSnapshot` reports instead of throwing; the heartbeat timer handler absorbs failures too |
| D-28 | M5-I-19 (S5 verifier) | Timeline `?after=` compared the client's `recordedAt`, so an event replayed after an outage (old client time) was never returned to an incremental refresh | **Fixed** — `after` compares the server receive time (`ReceivedAt`, captures `UploadedAt`) |
| D-29 | M5-I-20 (S5 verifier) | A reconnecting heartbeat that collided with a submit/force-submit reloaded the session and re-applied only `LastHeartbeatAt`: the `Reconnected` event was stored but the session stayed flagged Disconnected (and the next beat recorded a second reconnect) | **Fixed** — the retry re-applies the reconnect; the seat-mismatch event also records `ReceivedAt` |
| D-30 | M10-E-06 (S6) | A Live Monitor page that was opened or reloaded mid-exam started empty and lost every tile's history, and the drawer timeline lived only in that browser tab | **Fixed** — `GET /api/exams/{id}/live` snapshot + server timeline in the drawer (M10-I-11, M10-E-14) |
| D-31 | M10-I-15 (S6 verifier) | Any teacher could extend, force-end, edit or delete any exam (and create one in another teacher's section) — the Module 7 "M10 hardening" item was left open; other teachers also received the answer key from `GET /api/exams/{id}` | **Fixed** — `extend-time`, `force-submit-all`, `PUT` and `DELETE /api/exams/{id}` use `LoadManagedExamAsync` (section teacher or admin, else 403); create/edit check the target section; non-owner teachers get the proctor view of the questions (M10-I-15/16, M10-A-04) |
| D-32 | M10-I-17 (S6 verifier) | `PATCH /api/notifications/read-all` cleared a proctor's warning (so it never reached the exam window) without the `PROCTOR_WARNING_ACKNOWLEDGED` audit row | **Fixed** — read-all audits each warning it clears (`via: read-all`) |
| D-33 | M5-U (S6 verifier) | Desktop test `JpegEncoder_WorksOnAFrozenSourceOffTheUiThread` failed about 1 run in 5: it froze the bitmap on a pool thread, and freezing is thread-affine (passed only when `Task.Run` reused the test thread). Production was not affected (`ScreenCapture` freezes on the capturing thread) | **Fixed** — the test freezes on the creating thread; the encoder's doc states the contract |
| D-34 | M11-I-09 (S7) | Any teacher could read any exam's results and plagiarism report and override any answer's grade (results / plagiarism / override were not ownership-checked); overrides accepted any mark and were not audited | **Fixed** — owner (section teacher) or admin only (403 otherwise, 404 for an unknown exam); marks must be within 0…question marks; audit `GRADE_OVERRIDDEN` (M11-I-09, M11-I-17, M11-A-04) |
| D-35 | M11-I-05 (S7) | With `-O2` the compiler deleted the test's unused 1 GB allocation (and would delete an empty endless loop), so memory- and time-limit breaches went undetected | **Fixed by design** — both runners compile with `-O0`: the program does what the student wrote |
| D-36 | M11-I-22 (S7 verifier) | "Accept AI suggestions" also turned the grader's non-grades (code not compiled, theory without a model answer, compile check only — "0, Low, teacher review required") into final 0 marks | **Fixed** — `GradingService.NeedsManualMark`: such answers are skipped and reported as `needsManualMark` |
| D-37 | M11-I-23…25 (S7 verifier) | Any error outside the runner's retries (an LLM reply of an unexpected shape, the database refusing the result — e.g. an engine label over 64 characters) left the answer Pending for ever: re-run on every restart, results page polling indefinitely | **Fixed** — the LLM client treats any malformed reply as "no answer" (offline fallback); engine labels are cut to the column; an unexpected failure marks the row Failed (same queue version only) and announces it |
| D-38 | M11-E-07 (S7 verifier) | Saving an exam from the edit page reset each test case's category, ignore-case and numeric tolerance (set through the API; the wizard has no editor for them) | **Fixed** — the wizard carries the fields through |

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
