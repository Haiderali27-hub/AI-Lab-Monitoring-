import type { AcademicSection, BatchUploadResult, Department, StudentBindingStatus, UpdateUserPayload, UserListItem, UserSummary } from '../types'
import { apiClient, unwrap } from './client'

type CreateUserPayload = {
  username: string
  email: string
  password: string
  departmentId: string | null
  sectionId: string | null
}

type CreateDepartmentPayload = {
  name: string
  code: string
}

type CreateSectionPayload = {
  departmentId: string
  name: string
  code: string
  academicYear: string
  semester: string | null
}

export type OrganizationInfo = {
  id: string
  name: string
  contactEmail: string
}

export async function getOrganization(): Promise<OrganizationInfo> {
  return unwrap<OrganizationInfo>(apiClient.get('/api/admin/organization'))
}

export async function createTeacher(payload: CreateUserPayload): Promise<UserSummary> {
  return unwrap<UserSummary>(apiClient.post('/api/admin/users/teachers', payload))
}

export async function createStudent(payload: CreateUserPayload): Promise<UserSummary> {
  return unwrap<UserSummary>(apiClient.post('/api/admin/users/students', payload))
}

export async function getTeachers(): Promise<UserListItem[]> {
  return unwrap<UserListItem[]>(apiClient.get('/api/admin/users/teachers'))
}

export async function getStudents(): Promise<UserListItem[]> {
  return unwrap<UserListItem[]>(apiClient.get('/api/admin/users/students'))
}

export async function getDepartments(): Promise<Department[]> {
  return unwrap<Department[]>(apiClient.get('/api/admin/hierarchy/departments'))
}

export async function createDepartment(payload: CreateDepartmentPayload): Promise<Department> {
  return unwrap<Department>(apiClient.post('/api/admin/hierarchy/departments', payload))
}

export async function getSections(): Promise<AcademicSection[]> {
  return unwrap<AcademicSection[]>(apiClient.get('/api/admin/hierarchy/sections'))
}

export async function createSection(payload: CreateSectionPayload): Promise<AcademicSection> {
  return unwrap<AcademicSection>(apiClient.post('/api/admin/hierarchy/sections', payload))
}

export async function toggleUserActive(userId: string): Promise<void> {
  await unwrap<{ id: string; isActive: boolean }>(apiClient.put(`/api/admin/users/${userId}/toggle-active`, {}))
}

export async function deleteUser(userId: string): Promise<void> {
  await unwrap<object>(apiClient.delete(`/api/admin/users/${userId}`))
}

export async function updateUser(userId: string, payload: UpdateUserPayload): Promise<void> {
  await unwrap<object>(apiClient.put(`/api/admin/users/${userId}`, payload))
}

export async function resetUserPassword(userId: string, newPassword: string): Promise<void> {
  await unwrap<object>(apiClient.post(`/api/admin/users/${userId}/reset-password`, { newPassword }))
}

export async function getStudentBindings(): Promise<StudentBindingStatus[]> {
  return unwrap<StudentBindingStatus[]>(apiClient.get('/api/admin/students/device-bindings'))
}

export async function resetStudentBinding(studentId: string): Promise<void> {
  await unwrap<boolean>(apiClient.post(`/api/admin/students/${studentId}/reset-binding`, {}))
}

export async function forceStudentLogout(studentId: string): Promise<void> {
  await unwrap<boolean>(apiClient.post(`/api/admin/students/${studentId}/force-logout`, {}))
}

export async function uploadStudentCsv(file: File): Promise<BatchUploadResult> {
  const formData = new FormData()
  formData.append('file', file)

  return unwrap<BatchUploadResult>(
    apiClient.post('/api/admin/students/batch-upload', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    }),
  )
}
