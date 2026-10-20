import { test, expect } from '@playwright/test';
import { uniqueTag } from '../support/env';
import { StaffApi } from '../support/staffApi';
import { StudentClient } from '../support/studentClient';

test.describe('M7 — User & Role Management', () => {
  test('M7-A-01 Create user', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const tag = uniqueTag('u');
    const res = await admin.createUserRaw({ name: `API User ${tag}`, email: `${tag}@e2e.smartexam.test`, password: 'Passw0rd!', role: 'Teacher' });
    expect(res.status()).toBe(201);
    const body = await res.json();
    expect(body.userId).toBeTruthy();
    expect(body.role).toBe('Teacher');
    expect(JSON.stringify(body)).not.toMatch(/passwordHash|salt/i);

    // The new account can log in
    const login = await StaffApi.login(request, `${tag}@e2e.smartexam.test`, 'Passw0rd!');
    expect(login.role).toBe('Teacher');
  });

  test('M7-A-02 Duplicate email rejected', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const res = await admin.createUserRaw({ name: 'Dup', email: user.email, password: 'Passw0rd!', role: 'Student' });
    expect([400, 409]).toContain(res.status());
  });

  test('M7-A-03 Teacher cannot create users', async ({ request }) => {
    const teacher = await StaffApi.teacher(request);
    const tag = uniqueTag('u');
    const res = await teacher.createUserRaw({ name: 'Nope', email: `${tag}@e2e.smartexam.test`, password: 'Passw0rd!', role: 'Admin' });
    expect(res.status()).toBe(403);
  });

  test('M7-A-04 Deactivate kills sessions', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const user = await admin.createStudent();
    const student = await new StudentClient(request, user.email, user.password).signIn();
    expect((await student.me()).status()).toBe(200);

    expect((await admin.deactivate(user.userId)).status()).toBe(200);
    expect((await student.me()).status()).toBe(401);
    // ...and cannot simply log in again
    expect((await student.login()).status()).toBe(401);
  });
});
