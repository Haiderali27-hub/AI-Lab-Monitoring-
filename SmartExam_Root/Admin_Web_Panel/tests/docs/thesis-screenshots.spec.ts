import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test, expect, type Browser, type Page } from '@playwright/test';
import { SEED } from '../support/env';
import { StaffApi, type CreatedUser } from '../support/staffApi';
import { StudentClient } from '../support/studentClient';
import { acceptDialogs, signInStudentWeb, signInWeb } from '../support/scenario';

// ─────────────────────────────────────────────────────────────────────────────
// Thesis figures (Chapter 6): real captures of the running web panel.
// NOT part of the normal suite — run with `npm run docs:screenshots` (playwright.docs.config.ts).
//
// The scenario first builds realistic demo state over HTTP, exactly as the desktop client and
// staff would (tests/support): students bind their lab PCs, pass the readiness check, sit two
// quizzes and the seeded "Mid-Term Lab Exam (Demo)", stream heartbeats / violations / a webcam
// flag / screen captures, one gets a proctor warning, answers are graded (coding answers really
// compiled and run), a teacher overrides a mark, and the exam is ended. Then every page is shot.
//
// The screen captures uploaded on behalf of the simulated students are rendered here (a code
// editor / a browser window), since the WPF desktop client itself is never launched.
// ─────────────────────────────────────────────────────────────────────────────

const OUT_DIR = process.env.SMARTEXAM_SHOTS_DIR ?? path.join(os.tmpdir(), 'smartexam-thesis-shots');
const MAX_BYTES = 250 * 1024;
const PASSWORD = 'Student@123';

test.describe.configure({ mode: 'serial' });

// ── capture helpers ──────────────────────────────────────────────────────────
const shots: { name: string; file: string; kb: number; size: string; route: string }[] = [];

/** How much taller the page's inner scroll container (the staff layout's <main>) is than its box. */
function innerOverflow(page: Page): Promise<number> {
  return page.evaluate(() => {
    let extra = 0;
    for (const el of Array.from(document.querySelectorAll<HTMLElement>('main, main *, [class*="overflow-y-auto"]'))) {
      if (el.closest('[data-testid="student-drawer"]')) continue;
      const oy = getComputedStyle(el).overflowY;
      if ((oy === 'auto' || oy === 'scroll') && el.scrollHeight > el.clientHeight + 4) extra = Math.max(extra, el.scrollHeight - el.clientHeight);
    }
    return extra;
  });
}

/**
 * Waits for fonts, spinners and toasts to settle, then saves `name`.png (or .jpg q85 if the PNG is over 250 KB).
 * `tall`: grows the viewport (up to 2400 px) so a page that scrolls inside its layout fits in one shot;
 * `width`: a wider viewport for tables that overflow 1440 px. The viewport is restored afterwards.
 */
async function snap(page: Page, name: string, opts: { fullPage?: boolean; tall?: boolean; width?: number; keepToasts?: boolean } = {}) {
  const base = page.viewportSize() ?? { width: 1440, height: 900 };
  if (opts.width) {
    await page.setViewportSize({ width: opts.width, height: base.height });
    await page.waitForTimeout(400);
  }
  if (opts.tall) {
    for (let i = 0; i < 3; i++) {
      const extra = await innerOverflow(page);
      if (extra <= 0) break;
      const cur = page.viewportSize()!;
      await page.setViewportSize({ width: cur.width, height: Math.min(2400, cur.height + extra) });
      await page.waitForTimeout(400);
    }
  }
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 20_000 });
  if (!opts.keepToasts) await expect(page.locator('div.fixed.bottom-6.right-6 > div')).toHaveCount(0, { timeout: 10_000 });
  await expect(page.getByText(/something went wrong|failed to load|cannot load/i)).toHaveCount(0);
  await page.waitForTimeout(400); // let transitions finish
  try {
    await capture(page, name, !!opts.fullPage);
  } finally {
    if (opts.tall || opts.width) await page.setViewportSize(base);
  }
}

async function capture(page: Page, name: string, fullPage: boolean) {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const png = await page.screenshot({ fullPage, animations: 'disabled', caret: 'hide' });
  let file = path.join(OUT_DIR, `${name}.png`);
  let bytes = png.length;
  if (bytes > MAX_BYTES) {
    const jpg = await page.screenshot({ fullPage, animations: 'disabled', caret: 'hide', type: 'jpeg', quality: 85 });
    file = path.join(OUT_DIR, `${name}.jpg`);
    bytes = jpg.length;
    fs.writeFileSync(file, jpg);
    if (fs.existsSync(path.join(OUT_DIR, `${name}.png`))) fs.unlinkSync(path.join(OUT_DIR, `${name}.png`));
  } else {
    fs.writeFileSync(file, png);
    if (fs.existsSync(path.join(OUT_DIR, `${name}.jpg`))) fs.unlinkSync(path.join(OUT_DIR, `${name}.jpg`));
  }
  const vp = page.viewportSize()!;
  const height = fullPage ? await page.evaluate(() => document.documentElement.scrollHeight) : vp.height;
  shots.push({ name, file, kb: Math.round(bytes / 102.4) / 10, size: `${vp.width}x${Math.max(height, vp.height)}`, route: new URL(page.url()).pathname + new URL(page.url()).search });
}

/** Renders `html` at 1280×720 and returns it as a JPEG — the "student's screen" the simulated desktop client uploads. */
async function renderCapture(browser: Browser, html: string): Promise<Buffer> {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  const p = await ctx.newPage();
  await p.setContent(html);
  const buf = await p.screenshot({ type: 'jpeg', quality: 70 });
  await ctx.close();
  return buf;
}

function editorScreen(file: string, code: string, student: string): string {
  const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const lines = code.split('\n').map((l, i) => `<div><span class="n">${i + 1}</span>${esc(l) || '&nbsp;'}</div>`).join('');
  return `<html><body style="margin:0;font-family:Segoe UI,Arial;background:#1e1e1e;color:#d4d4d4">
  <div style="height:30px;background:#323233;color:#ccc;font-size:12px;display:flex;align-items:center;padding:0 12px">${file} — Visual Studio Code</div>
  <div style="display:flex;height:666px">
    <div style="width:48px;background:#333"></div>
    <div style="width:220px;background:#252526;font-size:12px;padding:10px;color:#bbb">EXPLORER<br><br>&#9662; LAB-EXAM<br>&nbsp;&nbsp;&nbsp;${file}<br>&nbsp;&nbsp;&nbsp;input.txt</div>
    <div style="flex:1">
      <div style="height:34px;background:#2d2d2d;font-size:12px;display:flex;align-items:center;padding:0 14px;color:#fff;border-top:1px solid #0078d4;width:140px">${file}</div>
      <pre style="margin:0;padding:12px 0;font:14px/21px Consolas,monospace">${lines}</pre>
    </div>
  </div>
  <div style="height:24px;background:#0078d4;color:#fff;font-size:12px;display:flex;align-items:center;padding:0 12px">C++ &nbsp;·&nbsp; UTF-8 &nbsp;·&nbsp; ${student}</div>
  <style>.n{display:inline-block;width:44px;color:#858585;text-align:right;margin-right:18px}</style></body></html>`;
}

function browserScreen(query: string): string {
  return `<html><body style="margin:0;font-family:Segoe UI,Arial;background:#fff">
  <div style="height:38px;background:#dee1e6;display:flex;align-items:flex-end;padding-left:10px">
    <div style="background:#fff;border-radius:8px 8px 0 0;padding:8px 16px;font-size:12px;width:220px">${query} - Search</div></div>
  <div style="height:40px;border-bottom:1px solid #ddd;display:flex;align-items:center;padding:0 16px">
    <div style="flex:1;background:#f1f3f4;border-radius:16px;padding:7px 16px;font-size:13px;color:#333">search?q=${encodeURIComponent(query)}</div></div>
  <div style="padding:28px 160px;font-size:14px;color:#222">
    <div style="font-size:22px;margin-bottom:18px">${query}</div>
    ${['Find the maximum element in an array (C++) - tutorial', 'std::max_element - reference', 'How to read n integers from stdin in C++?']
      .map(t => `<div style="margin-bottom:22px"><div style="color:#1a0dab;font-size:18px">${t}</div><div style="color:#4d5156">Example code and explanation for beginners ...</div></div>`).join('')}
  </div></body></html>`;
}

// ── demo answers ─────────────────────────────────────────────────────────────
const CODE_ALI = `#include <iostream>
using namespace std;

int findMax(int arr[], int n) {
    int best = arr[0];
    for (int i = 1; i < n; i++)
        if (arr[i] > best) best = arr[i];
    return best;
}

int main() {
    int n;
    cin >> n;
    int arr[1000];
    for (int i = 0; i < n; i++) cin >> arr[i];
    cout << findMax(arr, n);
    return 0;
}`;
// Sara's answer: Ali's program with renamed identifiers — the plagiarism detector normalises names.
const CODE_SARA = CODE_ALI.replace(/best/g, 'top').replace(/\barr\b/g, 'nums').replace(/findMax/g, 'largest');
// Usman's answer starts the maximum at 0: passes the visible test, fails the hidden all-negative one.
const CODE_USMAN = `#include <iostream>
using namespace std;

int main() {
    int n, x, mx = 0;
    cin >> n;
    for (int i = 0; i < n; i++) {
        cin >> x;
        if (x > mx) mx = x;
    }
    cout << mx << endl;
}`;
const CODE_AYESHA = `#include <iostream>
#include <vector>
#include <algorithm>
using namespace std;

int main() {
    int n;
    cin >> n;
    vector<int> v(n);
    for (auto &e : v) cin >> e;
    cout << *max_element(v.begin(), v.end());
    return 0;
}`;
// Fatima ran out of time: the program does not compile.
const CODE_FATIMA = `#include <iostream>
using namespace std;

int main() {
    int n;
    cin >> n
    int a[n];
    // TODO read the values and find the largest`;

const THEORY = {
  ali: 'A stack is a LIFO (last in, first out) structure: the last element pushed is the first popped, e.g. the undo history in a text editor or a pile of plates. A queue is FIFO (first in, first out): elements leave in the order they arrived, e.g. a print queue or people waiting at a ticket counter.',
  sara: 'Stack follows LIFO - last in first out, like a stack of books where you take the top one first. Queue follows FIFO - first in first out, like a line at the bank. Example: browser back button uses a stack, printer jobs use a queue.',
  usman: 'Stack and queue both store data. Stack is used in recursion.',
  ayesha: 'In a stack insertion and deletion happen at the same end (top), so it is LIFO; function call stacks use it. In a queue insertion is at the rear and deletion at the front, so it is FIFO; CPU scheduling and a supermarket checkout line are examples.',
  fatima: 'Stack is LIFO and queue is FIFO.',
};

// ── the scenario ─────────────────────────────────────────────────────────────
interface Sitter {
  key: 'ali' | 'sara' | 'usman' | 'ayesha' | 'fatima';
  user: CreatedUser;
  client: StudentClient;
  seat: string;      // workstation MachineNumber
  host: string;      // workstation HostName == the PC's Windows computer name
  beat: Parameters<StudentClient['heartbeat']>[0];
  active: boolean;   // keeps heartbeating while true
}

test('thesis screenshots', async ({ page, request, browser }) => {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const admin = await StaffApi.admin(request);
  const teacher = await StaffApi.teacher(request);
  const superAdmin = await StaffApi.superAdmin(request);

  // ── 1. people: seeded Ali + Sara, four more students, a second teacher and an invigilator ──
  const createUser = async (name: string, email: string, role: string, password = PASSWORD): Promise<CreatedUser> => {
    const res = await admin.createUserRaw({ name, email, password, role });
    expect(res.status(), await res.text()).toBe(201);
    return { name, email, password, role, userId: (await res.json()).userId };
  };
  const usman = await createUser('Usman Tariq', 'usman.tariq@smartexam.com', 'Student');
  const ayesha = await createUser('Ayesha Malik', 'ayesha.malik@smartexam.com', 'Student');
  const fatima = await createUser('Fatima Noor', 'fatima.noor@smartexam.com', 'Student');
  const hamza = await createUser('Hamza Iqbal', 'hamza.iqbal@smartexam.com', 'Student');
  await createUser('Dr. Sana Rehman', 'sana.rehman@smartexam.com', 'Teacher', 'Teacher@123');
  await createUser('Mr. Kamran Ali (Invigilator)', 'kamran.ali@smartexam.com', 'Invigilator', 'Invig@123');
  const adminRes = await superAdmin.createUserRaw({ name: 'Exam Cell Admin', email: 'examcell@smartexam.com', password: 'Admin@123', role: 'Admin' });
  expect(adminRes.status(), await adminRes.text()).toBe(201);

  const ali: CreatedUser = { ...SEED.ali, role: 'Student', userId: '' };
  const sara: CreatedUser = { ...SEED.sara, role: 'Student', userId: '' };

  // ── 2. academic structure ──
  for (const d of ['Software Engineering', 'Electrical Engineering']) expect((await admin.createDepartment(d)).status()).toBe(201);
  const depts: { deptId: string; name: string }[] = await (await admin.get('/api/departments')).json();
  const deptId = (n: string) => depts.find(d => d.name === n)!.deptId;
  for (const [d, name, code] of [
    ['Computer Science', 'Object Oriented Programming', 'CS201'],
    ['Computer Science', 'Operating Systems', 'CS402'],
    ['Software Engineering', 'Software Construction', 'SE310'],
    ['Software Engineering', 'Software Quality Engineering', 'SE420'],
    ['Electrical Engineering', 'Digital Logic Design', 'EE215'],
  ] as const) expect((await admin.createCourse(deptId(d), name, code)).status()).toBe(201);

  // ── 3. labs and seats: named PCs so the seat check (host name) matches ──
  const labs = await admin.labs();
  const labA = labs.find(l => l.name === 'Lab A (Demo)')!;
  const sel = labs.find(l => l.name === 'Systems Engineering Lab')!;
  for (const [i, ws] of labA.workstations.entries()) {
    const n = Number(ws.machineNumber.slice(-1));
    expect((await admin.put(`/api/labs/${labA.labId}/workstations/${ws.workstationId}`,
      { machineNumber: ws.machineNumber, hostName: `LAB-A-PC0${n}`, ipAddress: ws.ipAddress })).status(), `ws ${i}`).toBe(200);
  }
  for (const [num, host, ip] of [['R1C3', 'LAB-A-PC03', '192.168.1.103'], ['R1C4', 'LAB-A-PC04', '192.168.1.104'],
    ['R2C1', 'LAB-A-PC05', '192.168.1.105'], ['R2C2', 'LAB-A-PC06', '192.168.1.106']])
    expect((await admin.post(`/api/labs/${labA.labId}/workstations`, { machineNumber: num, hostName: host, ipAddress: ip })).status()).toBe(201);
  for (let i = 1; i <= 6; i++)
    expect((await admin.post(`/api/labs/${sel.labId}/workstations`, { machineNumber: `SE-0${i}`, hostName: `SEL-PC0${i}`, ipAddress: `10.10.5.${10 + i}` })).status()).toBe(201);
  const seat = async (machineNumber: string) => (await admin.workstation(machineNumber)).workstationId;

  // ── 4. students bind their lab PCs (first desktop login binds the HWID) ──
  const hw = (n: number) => `BFEBFBFF000906EA-4C4C4544-00${40 + n}-8010-B4C04F4D3${n}32`;
  const sitters: Sitter[] = [
    { key: 'ali', user: ali, seat: 'R1C1', host: 'LAB-A-PC01', beat: {}, active: false, client: null! },
    { key: 'sara', user: sara, seat: 'R1C2', host: 'LAB-A-PC02', beat: {}, active: false, client: null! },
    { key: 'usman', user: usman, seat: 'R1C3', host: 'LAB-A-PC03', beat: {}, active: false, client: null! },
    { key: 'ayesha', user: ayesha, seat: 'R1C4', host: 'LAB-A-PC04', beat: {}, active: false, client: null! },
    { key: 'fatima', user: fatima, seat: 'R2C1', host: 'LAB-A-PC05', beat: {}, active: false, client: null! },
  ];
  for (const [i, s] of sitters.entries()) {
    s.client = await new StudentClient(request, s.user.email, s.user.password, hw(i + 1), s.host, s.key).signIn();
    s.user.userId = s.client.userId;
  }
  const by = (k: Sitter['key']) => sitters.find(s => s.key === k)!;

  // Heartbeats every 4 s for every sitting student (the desktop timer), so no tile ever goes "Disconnected".
  const pulse = setInterval(() => {
    for (const s of sitters) if (s.active) void s.client.heartbeat(s.beat).catch(() => undefined);
  }, 4_000);

  try {
    // ── 5. two earlier quizzes (ended) so the dashboards, analytics and the student trend have history ──
    const quizMcq = (body: string, options: string[], correct: number) => ({ type: 'Mcq' as const, bodyText: body, marks: 5, options, correctOptionIndex: correct });
    const quizzes = [
      {
        title: 'Lab Quiz 1 — Arrays & Loops',
        questions: [
          quizMcq('What is the index of the first element of a C++ array?', ['1', '0', '-1'], 1),
          { type: 'Theory' as const, bodyText: 'Explain what an array out-of-bounds access is and why it is dangerous.', marks: 10,
            referenceAnswer: 'Accessing an index outside 0..n-1 reads or writes memory that does not belong to the array; it is undefined behaviour and can crash the program or corrupt data.' },
        ],
        answers: {
          ali: ['1', 'Reading an index outside 0..n-1 touches memory that does not belong to the array. It is undefined behaviour: it can crash the program or silently corrupt data.'],
          sara: ['1', 'Out of bounds means using an index bigger than n-1 or negative. It is undefined behaviour and may crash.'],
          usman: ['0', 'When the loop goes too far.'],
          ayesha: ['1', 'An out-of-bounds access uses an index outside the valid range; the memory is not part of the array so the behaviour is undefined and data can be corrupted.'],
          fatima: ['1', 'Accessing memory outside the array, it can crash the program.'],
        } as Record<Sitter['key'], [string, string]>,
      },
      {
        title: 'Lab Quiz 2 — Stacks & Queues',
        questions: [
          quizMcq('Which operation removes the top element of a stack?', ['push', 'pop', 'enqueue'], 1),
          { type: 'Theory' as const, bodyText: 'Describe how a queue can be implemented with a circular array.', marks: 10,
            referenceAnswer: 'Keep front and rear indices into a fixed array and advance them modulo the capacity; the queue is full when (rear + 1) % capacity == front and empty when front == rear.' },
        ],
        answers: {
          ali: ['1', 'Use a fixed array with front and rear indices; advance them modulo the capacity. Empty when front == rear, full when (rear + 1) % capacity == front.'],
          sara: ['1', 'Front and rear pointers move forward modulo the array size so the space is reused.'],
          usman: ['2', 'Use an array and a loop.'],
          ayesha: ['1', 'Store items in an array of fixed capacity, keep front and rear indices and increment them modulo the capacity; the queue is full when (rear + 1) % capacity equals front.'],
          fatima: ['0', 'A circular array wraps around to the start when it reaches the end.'],
        } as Record<Sitter['key'], [string, string]>,
      },
    ];
    for (const q of quizzes) {
      const exam = await teacher.createExam({
        title: q.title, questions: q.questions, durationMinutes: 30, startTime: new Date(Date.now() - 10 * 60_000),
        studentIds: sitters.map(s => s.user.userId), allowedApps: '["code.exe"]', instructions: 'Short weekly lab quiz.',
      });
      for (const s of sitters) {
        await s.client.start(exam.examId);
        const [mcq, theory] = q.answers[s.key];
        expect((await s.client.saveAnswer(exam.questions[0].questionId, mcq)).status()).toBe(200);
        expect((await s.client.saveAnswer(exam.questions[1].questionId, theory)).status()).toBe(200);
        expect((await s.client.submit()).status()).toBe(200);
      }
      expect((await teacher.forceSubmitAll(exam.examId)).status()).toBe(200);
    }
    // An upcoming end-term exam (scheduled next week) for the teacher dashboard.
    await teacher.createExam({
      title: 'End-Term Lab Exam', startTime: new Date(Date.now() + 7 * 24 * 3600_000), durationMinutes: 120,
      studentIds: [...sitters.map(s => s.user.userId), hamza.userId], allowedApps: '["code.exe","codeblocks.exe"]',
      lockdownMode: 'Enforce', requireReadiness: true, requireWebcam: true, instructions: 'Closed book. Webcam required.',
    });

    // The seeded ended "Final Lab Exam (Demo)": the teacher marks its one (ungraded) answer.
    const finalExam = (await teacher.exams()).find(e => e.title === 'Final Lab Exam (Demo)')!;
    const [finalRow] = await teacher.results(finalExam.examId);
    expect((await teacher.post(`/api/answers/${finalRow.answers[0].answerId}/override`,
      { finalMarks: 16, note: 'Correct use of std::reverse; no edge-case handling.' })).status()).toBe(200);

    // ── 6. the seeded Mid-Term (Demo): the teacher adds model answers (Scheduled exams are editable) and the roster,
    //       then imports attendance, recalculates eligibility and maps seats ──
    const mid = (await teacher.exams()).find(e => e.title === 'Mid-Term Lab Exam (Demo)')!;
    const midId: string = mid.examId;
    const seeded = await teacher.examDetail(midId);
    const seededQ = [...seeded.questions].sort((x: any, y: any) => x.orderIndex - y.orderIndex);
    const roster = [...sitters.map(s => s.user.userId), hamza.userId];
    const edit = await teacher.put(`/api/exams/${midId}`, await teacher.examPayload({
      title: seeded.title, instructions: seeded.instructions, sectionId: seeded.sectionId,
      startTime: new Date(seeded.startTime.endsWith('Z') ? seeded.startTime : `${seeded.startTime}Z`),
      durationMinutes: seeded.durationMinutes, allowedApps: seeded.allowedApps, plagiarismThreshold: seeded.plagiarismThreshold,
      studentIds: roster,
      questions: seededQ.map((q: any) => ({
        type: q.type, bodyText: q.bodyText, marks: q.marks,
        options: q.optionsJson ? JSON.parse(q.optionsJson) : null, correctOptionIndex: q.correctOptionIndex ?? null,
        testCases: (q.testCases ?? []).map((t: any) => ({ input: t.input, expectedOutput: t.expectedOutput, isHidden: t.isHidden })),
        language: q.type === 'Coding' ? 'cpp' : null,
        referenceAnswer: q.type === 'Theory'
          ? 'A stack is LIFO: the last element pushed is the first popped (e.g. undo history, function call stack). A queue is FIFO: elements leave in arrival order (e.g. a print queue, a ticket line).'
          : q.type === 'Coding' ? CODE_ALI : null,
        rubricKeywords: q.type === 'Theory' ? 'LIFO, FIFO, push, pop, example' : null,
      })),
    }));
    expect(edit.status(), await edit.text()).toBe(200);
    // The edit re-creates the questions: the demo invigilator keeps the exam (proctors are separate).
    const { sectionId } = await teacher.examDetail(midId);
    const csv = ['email,total,attended',
      `${SEED.ali.email},40,38`, `${SEED.sara.email},40,35`, `${usman.email},40,32`,
      `${ayesha.email},40,37`, `${fatima.email},40,31`, `${hamza.email},40,24`].join('\n');
    expect((await teacher.importAttendance(sectionId, csv)).status()).toBe(200);
    expect((await teacher.recalculateEligibility(midId)).status()).toBe(200);
    for (const s of sitters) expect((await teacher.assignSeat(midId, s.user.userId, await seat(s.seat))).status()).toBe(200);
    expect((await teacher.assignSeat(midId, hamza.userId, await seat('R2C2'))).status()).toBe(200);

    const midDetail = await teacher.examDetail(midId);
    const qOf = (type: string) => midDetail.questions.find((q: any) => q.type === type).questionId as string;
    const [qCode, qTheory, qMcq] = [qOf('Coding'), qOf('Theory'), qOf('Mcq')];

    // ── 7. readiness check, then start (start-session activates the exam) ──
    for (const s of sitters) {
      const overrides = s.key === 'fatima'
        ? [{ name: 'Camera', passed: false, detail: 'No camera device found' }]
        : [];
      expect((await s.client.readiness(midId, overrides)).status()).toBe(200);
    }
    for (const s of sitters) {
      await s.client.start(midId);
      s.active = true;
      s.beat = { processListSnapshot: 'SmartExam,code,explorer', activeWindowTitle: 'SmartExam - Mid-Term Lab Exam (Demo)' };
      expect((await s.client.heartbeat(s.beat)).status()).toBe(200);
    }

    // Screen captures: everyone's editor; Usman's browser at the moment of his violation.
    const upload = async (s: Sitter, bytes: Buffer, trigger: 'Interval' | 'Violation' = 'Interval', reason?: string) =>
      expect((await s.client.screenshot({ bytes, trigger, reason })).status()).toBe(200);
    for (const [k, code] of [['ali', CODE_ALI], ['sara', CODE_SARA], ['usman', CODE_USMAN], ['ayesha', CODE_AYESHA], ['fatima', CODE_FATIMA]] as const)
      await upload(by(k), await renderCapture(browser, editorScreen('main.cpp', code, by(k).user.name)));

    // Answers saved while the exam runs (autosave).
    const save = async (s: Sitter, q: string, text: string) => expect((await s.client.saveAnswer(q, text)).status()).toBe(200);
    await save(by('ali'), qCode, CODE_ALI); await save(by('ali'), qTheory, THEORY.ali); await save(by('ali'), qMcq, '1');
    await save(by('sara'), qCode, CODE_SARA); await save(by('sara'), qTheory, THEORY.sara);
    await save(by('usman'), qCode, CODE_USMAN);
    await save(by('ayesha'), qCode, CODE_AYESHA); await save(by('ayesha'), qMcq, '1');
    await save(by('fatima'), qCode, CODE_FATIMA);

    // Sara: blocked Alt+Tab → Warning.
    expect((await by('sara').client.violation('KeyBlocked', 'Alt+Tab')).status()).toBe(200);
    // Usman: opened Chrome → reported, then killed by lockdown → Critical; violation capture of his screen.
    const u = by('usman');
    u.beat = { ...u.beat, processListSnapshot: 'SmartExam,code,explorer,chrome', activeWindowTitle: 'c++ max element of array - Google Chrome' };
    await u.client.heartbeat(u.beat);
    expect((await u.client.event('Violation', { reason: 'UnauthorizedProcess', detail: 'chrome (Reported)' })).status()).toBe(200);
    await upload(u, await renderCapture(browser, browserScreen('c++ max element of array')), 'Violation', 'UnauthorizedProcess');
    expect((await u.client.event('Violation', { reason: 'ProcessKilled', detail: 'chrome (Killed)' })).status()).toBe(200);
    u.beat = { ...u.beat, activeWindowTitle: 'SmartExam - Mid-Term Lab Exam (Demo)' };
    // Ayesha: the on-device webcam model sees a phone → Suspicious, camera badge on the tile.
    const a = by('ayesha');
    a.beat = { ...a.beat, cameraStatus: 'Phone' };
    await a.client.heartbeat(a.beat);
    expect((await a.client.event('Violation', { reason: 'PhoneDetected', detail: 'Phone in view (confidence 0.91)' })).status()).toBe(200);
    // Fatima: no keyboard / mouse input for 9 minutes.
    const f = by('fatima');
    f.beat = { ...f.beat, idleSeconds: 545 };
    await f.client.heartbeat(f.beat);
    // Ali finishes early.
    expect((await by('ali').client.submit()).status()).toBe(200);
    by('ali').active = false;
    // A proctor incident on Usman's session.
    expect((await teacher.createIncidentRaw(u.client.sessionId, 'High', 'Opened Google Chrome during the exam; the process was terminated by lockdown.')).status()).toBe(201);

    // ═══ live monitoring captures ═══
    await signInWeb(page, teacher);
    await page.goto('/teacher/live-monitor');
    await expect(page.getByText('Live Monitor — Mid-Term Lab Exam (Demo)')).toBeVisible();
    const tiles = page.getByTestId('student-tile');
    await expect(tiles).toHaveCount(5, { timeout: 20_000 });
    const tile = (name: string) => tiles.filter({ hasText: name });
    await expect(tile('Usman Tariq').getByTestId('tile-status')).toHaveText('Critical', { timeout: 10_000 });
    await expect(tile('Ayesha Malik').getByTestId('tile-camera')).toBeVisible({ timeout: 10_000 });
    await expect(tile('Sara Khan').getByTestId('tile-status')).toHaveText('Warning');
    await expect(tile('Ali Hassan').getByTestId('tile-status')).toHaveText('Submitted');
    for (const n of ['Sara Khan', 'Usman Tariq', 'Ayesha Malik', 'Fatima Noor'])
      await expect(tile(n).getByTestId('tile-thumbnail')).toBeVisible({ timeout: 15_000 });
    await page.waitForTimeout(1_500);
    await snap(page, 'teacher_live_monitor', { tall: true });

    // Drawer for Usman: send a proctor warning, then show captures + readiness, and the incident + timeline.
    await tile('Usman Tariq').click();
    const drawer = page.getByTestId('student-drawer');
    await expect(drawer).toContainText('Usman Tariq');
    await drawer.getByTestId('warning-message-input').fill('Close all other applications and stay in the exam window.');
    await drawer.getByTestId('send-warning-button').click();
    await expect(drawer.getByText('Warning Message Sent')).toBeVisible();
    await expect(drawer.getByTestId('activity-timeline')).toContainText('Close all other applications', { timeout: 15_000 });
    await expect(drawer.getByTestId('screenshot-carousel').locator('img')).toHaveCount(2, { timeout: 15_000 });
    await snap(page, 'teacher_live_drawer');
    await drawer.getByTestId('incident-list').evaluate(el => el.scrollIntoView({ block: 'start' }));
    await snap(page, 'teacher_live_drawer_timeline');
    await drawer.getByRole('button').first().click().catch(() => undefined); // close (X)

    // Teacher + admin dashboards while the exam is running.
    await page.goto('/teacher/dashboard');
    await expect(page.getByText('End-Term Lab Exam').first()).toBeVisible();
    await snap(page, 'teacher_dashboard');
    const adminCtx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
    const adminPage = await adminCtx.newPage();
    await adminPage.goto('/login');
    await adminPage.getByLabel(/email/i).fill(SEED.admin.email);
    await adminPage.getByLabel(/password/i).fill(SEED.admin.password);
    await adminPage.getByRole('button', { name: /sign in/i }).click();
    await adminPage.waitForURL(/\/admin\/dashboard/);
    await expect(adminPage.getByText('Mid-Term Lab Exam (Demo)').first()).toBeVisible();
    await snap(adminPage, 'admin_dashboard');

    // ═══ eligibility (attendance + seats) ═══
    await page.goto(`/teacher/eligibility?examId=${midId}`);
    await expect(page.getByRole('heading', { name: 'Eligibility Management' })).toBeVisible();
    await expect(page.locator('tbody tr')).toHaveCount(6);
    await expect(page.locator('tbody tr').filter({ hasText: 'Hamza Iqbal' })).toContainText('60%');
    await snap(page, 'teacher_eligibility', { tall: true, width: 1680 });

    // ── 8. everyone else submits; the teacher ends the exam (Fatima force-submitted) ──
    await save(by('sara'), qMcq, '1');
    await save(u, qTheory, THEORY.usman); await save(u, qMcq, '0');
    await save(a, qTheory, THEORY.ayesha);
    await save(f, qTheory, THEORY.fatima);
    for (const k of ['sara', 'usman', 'ayesha'] as const) {
      expect((await by(k).client.submit()).status()).toBe(200);
      by(k).active = false;
    }
    expect((await teacher.forceSubmitAll(midId)).status()).toBe(200);
    f.active = false;
    await teacher.gradedResults(midId, 180_000);

    // ═══ results, AI grading, override, plagiarism ═══
    await page.goto(`/teacher/results/${midId}`);
    const row = (name: string) => page.locator('tbody tr').filter({ hasText: name });
    await expect(row('Fatima Noor')).toBeVisible();
    await expect(page.getByText('grading…')).toHaveCount(0, { timeout: 60_000 });
    await snap(page, 'teacher_results');

    await row('Usman Tariq').getByRole('button', { name: 'Review' }).click();
    await expect(page.getByTestId('testcase-result').first()).toBeVisible({ timeout: 30_000 });
    await page.locator('tbody tr').filter({ hasText: 'MAX 20 MARKS' }).first().evaluate(el => el.scrollIntoView({ block: 'start' }));
    await page.mouse.wheel(0, -60);
    await snap(page, 'teacher_ai_grading');

    // Override Usman's theory mark: the teacher reviews the AI suggestion and gives partial credit.
    const theoryBlock = page.locator('div.grid').filter({ has: page.getByRole('heading', { name: /stack and a queue/i }) }).last();
    await theoryBlock.locator('input[type="number"]').fill('3');
    await theoryBlock.getByPlaceholder('e.g. Minor syntax error fixed (-1)').fill('Mentions recursion; no LIFO/FIFO definition or examples.');
    const dialogs = acceptDialogs(page);
    await theoryBlock.getByRole('button', { name: 'Save Grade Override' }).click();
    await expect.poll(() => dialogs.join('\n')).toContain('Grade override applied successfully');
    await expect(theoryBlock.getByText('Overridden')).toBeVisible();
    await theoryBlock.evaluate(el => el.scrollIntoView({ block: 'center' }));
    await snap(page, 'teacher_grade_override');

    await page.getByRole('button', { name: /plagiarism flags/i }).click();
    await expect(page.getByTestId('plagiarism-heatmap').locator('[data-cell]').first()).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('plagiarism-flag').first()).toBeVisible();
    await snap(page, 'teacher_plagiarism');
    await page.getByTestId('plagiarism-pair').first().evaluate(el => el.scrollIntoView({ block: 'center' }));
    await snap(page, 'teacher_plagiarism_pair');

    // ═══ analytics / reports ═══
    await page.goto(`/teacher/analytics/${midId}`);
    await expect(page.getByText(/average score/i)).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('plagiarism-heatmap').locator('[data-cell]').first()).toBeVisible({ timeout: 15_000 });
    await snap(page, 'teacher_exam_analytics');
    await page.getByTestId('plagiarism-heatmap').evaluate(el => el.scrollIntoView({ block: 'start' }));
    await page.mouse.wheel(0, -90);
    await snap(page, 'teacher_plagiarism_heatmap');

    await page.goto('/teacher/reports');
    await page.locator('select').selectOption({ label: 'Mid-Term Lab Exam (Demo)' });
    await page.getByRole('button', { name: /generate new report/i }).click();
    await expect(page.getByRole('button', { name: /download/i }).first()).toBeVisible({ timeout: 30_000 });
    await snap(page, 'teacher_reports');

    // ═══ create-exam wizard ═══
    const blur = () => page.locator('h3').first().click();
    await page.goto('/teacher/create-exam');
    await page.getByLabel(/exam title/i).fill('Data Structures Lab Exam 3');
    await page.getByLabel(/section/i).selectOption({ index: 1 });
    await page.getByLabel(/duration/i).fill('90');
    await page.getByLabel(/date/i).fill(new Date(Date.now() + 3 * 24 * 3600_000).toISOString().split('T')[0]);
    await page.getByLabel(/time/i).fill('10:00');
    await page.getByPlaceholder(/mention rules/i).fill('Closed book. Only Visual Studio Code may be used. Webcam must stay on for the whole exam.');
    await blur();
    await snap(page, 'teacher_create_exam_details', { tall: true });
    await page.getByRole('button', { name: /next step/i }).click();
    await page.getByRole('button', { name: /add question/i }).first().click();
    await page.locator('select').filter({ has: page.locator('option[value="Mcq"]') }).nth(0).selectOption('Coding');
    await page.getByPlaceholder(/question prompt|coding task/i).first().fill('Implement a stack using a linked list with push, pop and peek; print the popped values.');
    await page.getByPlaceholder('Model solution code...').first().fill('struct Node { int v; Node* next; };\n// push / pop / peek ...');
    await page.getByRole('button', { name: /add test case/i }).first().click();
    await page.getByPlaceholder('e.g. [5, 3], 4').first().fill('push 3\npush 7\npop\npop');
    await page.getByPlaceholder('e.g. 15').first().fill('7 3');
    await blur();
    await snap(page, 'teacher_create_exam_questions', { tall: true });
    await page.getByRole('button', { name: /next step/i }).click();
    await expect(page.getByText(/rules & integrity settings/i)).toBeVisible();
    await page.locator('#lockdown-mode').selectOption('Enforce');
    for (const id of ['require-readiness', 'require-webcam']) {
      const box = page.locator(`#${id}`);
      if (!(await box.isChecked())) await page.locator(`label[for="${id}"]`).click();
    }
    await page.getByRole('button', { name: /add allowed app/i }).click();
    await page.getByRole('option', { name: /visual studio code/i }).click();
    await blur();
    await snap(page, 'teacher_create_exam_rules', { tall: true });

    // ═══ student web portal (Ali) ═══
    const studentCtx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
    const studentPage = await studentCtx.newPage();
    await signInStudentWeb(studentPage, request, ali);
    await studentPage.goto('/student/performance');
    await expect(studentPage.getByText('Mid-Term Lab Exam (Demo)').first()).toBeVisible({ timeout: 15_000 });
    await snap(studentPage, 'student_performance', { fullPage: true });
    await studentPage.goto(`/student/exams/${midId}/result`);
    await expect(studentPage.getByText('Mid-Term Lab Exam (Demo)').first()).toBeVisible({ timeout: 15_000 });
    await snap(studentPage, 'student_exam_result', { fullPage: true });
    await studentCtx.close();

    // ═══ login + admin pages (last, so the audit log holds the whole story) ═══
    const loginCtx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
    const loginPage = await loginCtx.newPage();
    await loginPage.goto('/login');
    await loginPage.getByLabel(/email/i).fill(SEED.teacher.email);
    await loginPage.getByLabel(/password/i).fill(SEED.teacher.password);
    await loginPage.getByText(/sign in to your account/i).click();
    await snap(loginPage, 'login');
    await loginCtx.close();

    for (const [route, name, wait, tall, width] of [
      ['/admin/users', 'admin_users', 'Usman Tariq', true, undefined],
      ['/admin/labs', 'admin_labs_workstations', 'LAB-A-PC03', false, undefined],
      ['/admin/device-bindings', 'admin_device_bindings', 'LAB-A-PC03', true, 1680],
      ['/admin/academics', 'admin_academics', 'Software Engineering', false, undefined],
      ['/admin/audit-logs', 'admin_audit_logs', 'LOGIN_SUCCESS', false, undefined],
      ['/admin/analytics', 'admin_system_analytics', 'Exams Per Month', false, undefined],
    ] as const) {
      await adminPage.goto(route);
      await expect(adminPage.getByText(wait, { exact: false }).filter({ visible: true }).first()).toBeVisible({ timeout: 15_000 });
      await snap(adminPage, name, { tall, width });
    }
    await adminCtx.close();
  } finally {
    clearInterval(pulse);
    fs.writeFileSync(path.join(OUT_DIR, 'index.json'), JSON.stringify(shots, null, 2));
  }
});
