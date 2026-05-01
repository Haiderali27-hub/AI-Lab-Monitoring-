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

export function destinationForRole(role: unknown): string {
  const normalized = normalizeRole(role)
  if (normalized === 'SuperAdmin') return '/super-admin'
  return '/admin'
}
