import type { BatchUploadResult, StudentBindingStatus, UserSummary } from '../types'
import { apiClient, unwrap } from './client'

type CreateUserPayload = {
  username: string
  email: string
  password: string
}

export async function createTeacher(payload: CreateUserPayload): Promise<UserSummary> {
  return unwrap<UserSummary>(apiClient.post('/api/admin/users/teachers', payload))
}

export async function createStudent(payload: CreateUserPayload): Promise<UserSummary> {
  return unwrap<UserSummary>(apiClient.post('/api/admin/users/students', payload))
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