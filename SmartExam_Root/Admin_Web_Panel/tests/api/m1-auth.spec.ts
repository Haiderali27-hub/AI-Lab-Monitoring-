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
});
