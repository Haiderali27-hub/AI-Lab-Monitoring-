import { test, expect } from '@playwright/test';
import { examWithStudents } from '../support/scenario';
import { StaffApi } from '../support/staffApi';
import { API_URL } from '../support/env';

// Module 10 — the proctoring API behind the Live Monitor: incidents, the board snapshot and the apps a PC reported.
// (Hub methods — warnings, unlock, extra time — are covered by xUnit M10-I-* and the m10 E2E tests.)
test.describe('M10 — Live Proctoring API', () => {
  test('M10-A-01 Incident lifecycle, validation and scoping', async ({ request }) => {
    const { students, exam, teacher, admin } = await examWithStudents(request, 1);
    const sessionId = await students[0].start(exam.examId);

    const created = await teacher.createIncidentRaw(sessionId, 'High', 'Phone visible on desk');
    expect(created.status()).toBe(201);
    const incident = await created.json();
    expect(incident.status).toBe('Open');
    expect(incident.severity).toBe('High');
    expect(incident.studentName).toBeTruthy();

    expect((await teacher.updateIncidentRaw(incident.incidentId, { status: 'Escalated', notes: 'Needs the teacher' })).status()).toBe(200);
    const resolved = await teacher.updateIncidentRaw(incident.incidentId, { status: 'Resolved', notes: 'Phone handed in' });
    expect(resolved.status()).toBe(200);
    expect((await resolved.json()).statusNote).toBe('Phone handed in');

    // Resolved is final; unknown values are refused.
    const again = await teacher.updateIncidentRaw(incident.incidentId, { status: 'Open' });
    expect(again.status()).toBe(400);
    expect((await again.json()).code).toBe('INVALID_TRANSITION');
    expect((await teacher.updateIncidentRaw(incident.incidentId, { status: 'Banana' })).status()).toBe(400);
    expect((await teacher.createIncidentRaw(sessionId, 'Apocalyptic', 'x')).status()).toBe(400);
    expect((await teacher.createIncidentRaw(sessionId, 'Low', '   ')).status()).toBe(400);

    const list = await teacher.incidents(exam.examId);
    expect(list.map(i => i.incidentId)).toEqual([incident.incidentId]);
    expect(await teacher.incidents(exam.examId, 'Open')).toEqual([]);
    for (const eventType of ['INCIDENT_CREATED', 'INCIDENT_ESCALATED', 'INCIDENT_RESOLVED']) {
      const rows = (await admin.auditLogs(eventType)).filter((l: any) => l.entityId === incident.incidentId);
      expect(rows, eventType).toHaveLength(1);
    }

    // Another section's teacher and students get nothing.
    const otherUser = await admin.createUser('Teacher', 'Other');
    const other = await StaffApi.login(request, otherUser.email, otherUser.password);
    expect((await other.createIncidentRaw(sessionId, 'Low', 'x')).status()).toBe(403);
    expect((await other.incidentsRaw(exam.examId)).status()).toBe(403);
    const asStudent = await request.post(`${API_URL}/api/incidents`, {
      headers: { Authorization: `Bearer ${students[0].token}` },
      data: { sessionId, severity: 'Low', notes: 'x' },
    });
    expect(asStudent.status()).toBe(403);
  });

  test('M10-A-02 Board snapshot lists heartbeating students with their state', async ({ request }) => {
    const { students, users, exam, teacher, admin } = await examWithStudents(request, 2);
    const sessionId = await students[0].start(exam.examId);
    await students[1].start(exam.examId);                 // never heartbeats: not on the board yet
    expect((await students[0].heartbeat({ processListSnapshot: 'SmartExam,code', idleSeconds: 90, cameraStatus: 'Ok' })).status()).toBe(200);
    expect((await students[0].violation('KeyBlocked', 'Alt+Tab')).status()).toBe(200);
    expect((await students[0].screenshot()).status()).toBe(200);
    expect((await teacher.createIncidentRaw(sessionId, 'Low', 'Talking')).status()).toBe(201);

    const { students: board } = await teacher.live(exam.examId);
    expect(board).toHaveLength(1);
    const tile = board[0];
    expect(tile.studentName).toBe(users[0].name);
    expect(tile.sessionId).toBe(sessionId);
    expect(tile.violationCount).toBe(1);
    expect(tile.tileStatus).toBe('Warning');
    expect(tile.lastViolationType).toBe('KeyBlocked');
    expect(tile.processes).toBe('SmartExam,code');
    expect(tile.cameraStatus).toBe('Ok');
    expect(tile.latestScreenshot.screenshotId).toBeTruthy();
    expect(tile.openIncidents).toBe(1);
    expect(tile.unlockedUntilUtc).toBeNull();

    const otherUser = await admin.createUser('Teacher', 'Other');
    const other = await StaffApi.login(request, otherUser.email, otherUser.password);
    expect((await other.liveRaw(exam.examId)).status()).toBe(403);
  });

  test('M10-A-03 Apps seen during the session are remembered', async ({ request }) => {
    const { students, exam, teacher } = await examWithStudents(request, 1);
    const sessionId = await students[0].start(exam.examId);
    await students[0].heartbeat({ processListSnapshot: 'SmartExam,chrome' });
    await students[0].heartbeat({ processListSnapshot: 'SmartExam,code' });

    const res = await teacher.processesRaw(exam.examId, sessionId);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.current).toEqual(['code', 'SmartExam']);
    const chrome = body.seen.find((s: any) => s.name === 'chrome');
    expect(chrome.running).toBe(false);
  });
});
