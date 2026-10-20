import { test, expect } from '@playwright/test';
import { API_URL, SEED } from '../support/env';
import { StaffApi } from '../support/staffApi';
import { StudentClient } from '../support/studentClient';

test.describe('M1 — Authentication & Device Binding', () => {
  test('M1-A-01 Staff login success', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/auth/login`, { data: SEED.teacher });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.token).toMatch(/^[\w-]+\.[\w-]+\.[\w-]+$/); // JWT
    expect(body.role).toBe('Teacher');
    expect(body.email).toBe(SEED.teacher.email);
  });

  test('M1-A-02 Wrong password', async ({ request }) => {
    const before = new Date(Date.now() - 1000).toISOString();
    const res = await request.post(`${API_URL}/api/auth/login`, { data: { email: SEED.teacher.email, password: 'Wrong@999' } });
    expect(res.status()).toBe(401);
    expect(JSON.stringify(await res.json())).not.toContain('token');

    const admin = await StaffApi.admin(request);
    const failed = await admin.auditLogs('LOGIN_FAILED');
    const mine = failed.filter(l => l.createdAt >= before.slice(0, 19) && l.details.includes(SEED.teacher.email));
    expect(mine.length).toBeGreaterThan(0);
    expect(mine[0].details).toContain('bad_password');
  });

  test('M1-A-03 First student login binds device', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const client = new StudentClient(request, user.email, user.password, 'HWID-X-' + user.userId);

    const res = await client.login();
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data.deviceBound).toBe(true);
    expect(body.data.user.role).toBe('Student');

    const detail = await (await admin.get(`/api/users/${user.userId}`)).json();
    expect(detail.deviceBound).toBe(true);
    expect(detail.machineName).toBe(client.machineName);
  });

  test('M1-A-04 Device mismatch blocked', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const client = new StudentClient(request, user.email, user.password, 'HWID-X');
    expect((await client.login('HWID-X')).status()).toBe(200);

    const res = await client.login('HWID-Y');
    expect(res.status()).toBe(401);
    const body = await res.json();
    expect(body.success).toBe(false);
    expect(body.code).toBe('DEVICE_MISMATCH');

    const audit = await admin.auditLogs('DEVICE_MISMATCH');
    expect(audit.some(l => l.entityId === user.userId)).toBe(true);
  });

  test('M1-A-05 Admin reset binding allows new device', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const client = new StudentClient(request, user.email, user.password);
    expect((await client.login('HWID-X')).status()).toBe(200);
    expect((await client.login('HWID-Y')).status()).toBe(401);

    expect((await admin.resetDeviceBinding(user.userId)).status()).toBe(200);
    expect((await client.login('HWID-Y')).status()).toBe(200);
    // ...and the new device is now the bound one
    expect((await client.login('HWID-X')).status()).toBe(401);
  });

  test('M1-A-06 Missing HWID rejected', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();

    const omitted = await StudentClient.rawLogin(request, { usernameOrEmail: user.email, password: user.password });
    expect(omitted.status()).toBe(400);
    const empty = await StudentClient.rawLogin(request, { usernameOrEmail: user.email, password: user.password, hardwareFingerprint: '' });
    expect(empty.status()).toBe(400);

    // No binding was created by the rejected attempts
    const detail = await (await admin.get(`/api/users/${user.userId}`)).json();
    expect(detail.deviceBound).toBe(false);
  });

  // ── Refresh tokens, roll numbers (M1-I-01…06 are the in-process versions; these run black-box) ──

  test('M1-A-07 Refresh rotates the pair; replaying the old refresh token revokes the family', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const client = new StudentClient(request, user.email, user.password);
    expect((await client.login()).status()).toBe(200);
    const r1 = client.refreshToken;
    expect(r1.length).toBeGreaterThanOrEqual(32);

    expect((await client.refresh()).status()).toBe(200);
    expect(client.refreshToken).not.toBe(r1);
    expect((await client.me()).status()).toBe(200);

    // Replay of the rotated token = theft signal → 401, and the legitimate successor dies with it.
    const replay = await client.refresh(r1);
    expect(replay.status()).toBe(401);
    expect((await replay.json()).code).toBe('REFRESH_TOKEN_REUSED');
    expect((await client.me()).status()).toBe(401);
    expect((await client.refresh()).status()).toBe(401);

    const audit = await admin.auditLogs('REFRESH_TOKEN_REUSE');
    expect(audit.some(l => l.entityId === user.userId)).toBe(true);
  });

  test('M1-A-08 Logout and force-logout revoke the refresh token', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const client = new StudentClient(request, user.email, user.password);

    await client.signIn();
    expect((await client.logout()).status()).toBe(200);
    expect((await client.refresh()).status()).toBe(401);

    await client.signIn();
    expect((await admin.post(`/api/users/${user.userId}/force-logout`)).status()).toBe(200);
    expect((await client.refresh()).status()).toBe(401);
  });

  test('M1-A-09 Staff login returns a refresh token and a short-lived access token', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/auth/login`, { data: SEED.teacher });
    const body = await res.json();
    expect(body.refreshToken).toBeTruthy();
    const payload = JSON.parse(Buffer.from(body.token.split('.')[1], 'base64url').toString());
    expect(payload.exp - payload.iat).toBe(30 * 60);

    const refreshed = await request.post(`${API_URL}/api/auth/refresh`, { data: { refreshToken: body.refreshToken } });
    expect(refreshed.status()).toBe(200);
    expect((await refreshed.json()).data.user.role).toBe('Teacher');
  });

  test('M1-A-10 Student logs in with roll number, not with display name', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const roll = `R-${user.userId.slice(0, 8)}`.toUpperCase();
    expect((await admin.put(`/api/users/${user.userId}`, { name: user.name, email: user.email, role: 'Student', rollNumber: roll.toLowerCase() })).status()).toBe(200);

    const byRoll = await StudentClient.rawLogin(request, { usernameOrEmail: roll, password: user.password, hardwareFingerprint: 'HWID-ROLL' });
    expect(byRoll.status()).toBe(200);
    expect((await byRoll.json()).data.user.email).toBe(user.email);

    const byName = await StudentClient.rawLogin(request, { usernameOrEmail: user.name, password: user.password, hardwareFingerprint: 'HWID-ROLL' });
    expect(byName.status()).toBe(401);

    // Roll numbers are unique
    const other = await admin.createStudent();
    expect((await admin.put(`/api/users/${other.userId}`, { name: other.name, email: other.email, role: 'Student', rollNumber: roll })).status()).toBe(409);
  });
});
