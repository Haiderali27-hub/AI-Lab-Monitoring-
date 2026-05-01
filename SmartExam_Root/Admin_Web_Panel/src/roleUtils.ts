import type { SystemRole } from './types'

export function normalizeRole(role: unknown): SystemRole | null {
  const value = String(role ?? '')
    .replace(/[\s_-]/g, '')
    .toLowerCase()

  if (value === 'superadmin' || value === '0') return 'SuperAdmin'
  if (value === 'organizationadmin' || value === 'orgadmin' || value === 'admin' || value === '1') return 'OrganizationAdmin'
  if (value === 'teacher' || value === '2') return 'Teacher'
  if (value === 'student' || value === '3') return 'Student'

  return null
}

export function formatRole(role: unknown): string {
  const normalized = normalizeRole(role)
  if (normalized === 'SuperAdmin') return 'Super Admin'
  if (normalized === 'OrganizationAdmin') return 'Organization Admin'
  return normalized ?? String(role ?? 'User')
}

export function formatExamSessionStatus(status: unknown): string {
  const value = String(status ?? '')
    .replace(/[\s_-]/g, '')
    .toLowerCase()

  if (value === 'notstarted' || value === '1') return 'Not Started'
  if (value === 'inprogress' || value === '2') return 'In Progress'
  if (value === 'submitted' || value === '3') return 'Submitted'
  if (value === 'terminated' || value === '4') return 'Terminated'

  return String(status ?? 'Unknown')
}

export function destinationForRole(role: unknown): string {
  const normalized = normalizeRole(role)
  if (normalized === 'SuperAdmin') return '/super-admin'
  return '/admin'
}
