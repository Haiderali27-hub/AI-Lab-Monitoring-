import apiClient from './client';
import type { Course, Department } from '../types';

/** Module 7 — departments and courses (reads: any staff; writes: Admin / SuperAdmin). */
export const academicsApi = {
  getDepartments: () =>
    apiClient.get<Department[]>('/departments').then(r => r.data),

  createDepartment: (name: string) =>
    apiClient.post<Department>('/departments', { name }).then(r => r.data),

  updateDepartment: (deptId: string, name: string) =>
    apiClient.put<Department>(`/departments/${deptId}`, { name }).then(r => r.data),

  deleteDepartment: (deptId: string) =>
    apiClient.delete(`/departments/${deptId}`),

  getCourses: (deptId?: string) =>
    apiClient.get<Course[]>('/courses', { params: deptId ? { deptId } : undefined }).then(r => r.data),

  createCourse: (deptId: string, name: string, code: string) =>
    apiClient.post<Course>('/courses', { deptId, name, code }).then(r => r.data),

  updateCourse: (courseId: string, deptId: string, name: string, code: string) =>
    apiClient.put<Course>(`/courses/${courseId}`, { deptId, name, code }).then(r => r.data),

  deleteCourse: (courseId: string) =>
    apiClient.delete(`/courses/${courseId}`),
};
