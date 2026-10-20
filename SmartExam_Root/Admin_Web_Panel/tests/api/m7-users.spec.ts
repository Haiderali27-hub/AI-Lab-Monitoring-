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

  test('M7-A-05 Only a SuperAdmin creates administrators', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const tag = uniqueTag('a');
    const denied = await admin.createUserRaw({ name: `Admin ${tag}`, email: `${tag}@e2e.smartexam.test`, password: 'Passw0rd!', role: 'Admin' });
    expect(denied.status()).toBe(403);
    expect((await denied.json()).code).toBe('SUPERADMIN_REQUIRED');

    const superAdmin = await StaffApi.superAdmin(request);
    const created = await superAdmin.createUserRaw({ name: `Admin ${tag}`, email: `${tag}@e2e.smartexam.test`, password: 'Passw0rd!', role: 'Admin' });
    expect(created.status()).toBe(201);
    // ...and that new Admin is now out of reach for the ordinary admin.
    const { userId } = await created.json();
    expect((await admin.deactivate(userId)).status()).toBe(403);
    expect((await superAdmin.deactivate(userId)).status()).toBe(200);
  });

  test('M7-A-06 Departments and courses CRUD', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const tag = uniqueTag('d');
    const dept = await admin.createDepartment(`Dept ${tag}`);
    expect(dept.status()).toBe(201);
    const { deptId } = await dept.json();
    expect((await admin.createDepartment(`DEPT ${tag}`)).status()).toBe(409); // names are case-insensitive unique

    const course = await admin.createCourse(deptId, 'Operating Systems', `os-${tag}`);
    expect(course.status()).toBe(201);
    const { courseId, code } = await course.json();
    expect(code).toBe(`OS-${tag}`.toUpperCase());
    expect((await admin.createCourse(deptId, 'Duplicate', `OS-${tag}`)).status()).toBe(409);

    const listed: any[] = await (await admin.get(`/api/courses?deptId=${deptId}`)).json();
    expect(listed.map(c => c.courseId)).toEqual([courseId]);
    expect((await admin.delete(`/api/departments/${deptId}`)).status()).toBe(409); // still has a course

    // Teachers read, never write.
    const teacher = await StaffApi.teacher(request);
    expect((await teacher.get('/api/departments')).status()).toBe(200);
    expect((await teacher.createCourse(deptId, 'Nope', `NO-${tag}`)).status()).toBe(403);

    expect((await admin.put(`/api/courses/${courseId}`, { deptId, name: 'Operating Systems II', code: `OS2-${tag}` })).status()).toBe(200);
    expect((await admin.delete(`/api/courses/${courseId}`)).status()).toBe(204);
    expect((await admin.delete(`/api/departments/${deptId}`)).status()).toBe(204);
    expect((await admin.get(`/api/departments/${deptId}`)).status()).toBe(404);
  });

  test('M7-A-07 Proctor assignment scopes what an invigilator can see', async ({ request }) => {
    const admin = await StaffApi.admin(request);
    const teacher = await StaffApi.teacher(request);
    const invigilatorUser = await admin.createInvigilator();
    const assigned = await teacher.createExam({ title: `Proctored ${uniqueTag('x')}` });
    const other = await teacher.createExam({ title: `Unassigned ${uniqueTag('x')}` });

    expect((await teacher.assignProctor(assigned.examId, invigilatorUser.userId)).status()).toBe(201);
    expect((await teacher.assignProctor(assigned.examId, invigilatorUser.userId)).status()).toBe(200); // idempotent
    expect((await teacher.proctors(assigned.examId)).map(p => p.userId)).toEqual([invigilatorUser.userId]);

    const invigilator = await StaffApi.login(request, invigilatorUser.email, invigilatorUser.password);
    expect((await invigilator.exams()).map(e => e.examId)).toEqual([assigned.examId]);
    expect((await invigilator.get(`/api/exams/${other.examId}`)).status()).toBe(404);
    const detail = await invigilator.examDetail(assigned.examId);
    expect(JSON.stringify(detail)).not.toContain('secret reference solution'); // no answer key for proctors
    expect((await invigilator.assignProctor(assigned.examId, invigilatorUser.userId)).status()).toBe(403);

    expect((await teacher.unassignProctor(assigned.examId, invigilatorUser.userId)).status()).toBe(204);
    expect(await invigilator.exams()).toEqual([]);
    expect((await admin.auditLogs('PROCTOR_ASSIGNED')).some(l => l.entityId === assigned.examId)).toBe(true);
  });

  test('M7-A-08 Demoting an admin ends their sessions', async ({ request }) => {
    const superAdmin = await StaffApi.superAdmin(request);
    const adminUser = await superAdmin.createUser('Admin', 'Demoted');
    const asAdmin = await StaffApi.login(request, adminUser.email, adminUser.password);
    expect((await asAdmin.get('/api/audit-logs?take=1')).status()).toBe(200);

    const demote = await superAdmin.put(`/api/users/${adminUser.userId}`, { name: adminUser.name, email: adminUser.email, role: 'Teacher' });
    expect(demote.status()).toBe(200);
    // The old token carried the Admin role: it must stop working now, not when it expires.
    expect((await asAdmin.get('/api/audit-logs?take=1')).status()).toBe(401);

    const relogin = await StaffApi.login(request, adminUser.email, adminUser.password);
    expect(relogin.role).toBe('Teacher');
    expect((await relogin.get('/api/audit-logs?take=1')).status()).toBe(403);
  });
});
