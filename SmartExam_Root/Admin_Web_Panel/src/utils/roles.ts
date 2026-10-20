import type { UserRole } from '../types';

/** Every role the web panel can sign in (a cached session with any other value is discarded). */
export const KNOWN_ROLES: readonly UserRole[] = ['SuperAdmin', 'Admin', 'Teacher', 'Invigilator', 'Student'];

export const isKnownRole = (role: unknown): role is UserRole =>
  typeof role === 'string' && (KNOWN_ROLES as readonly string[]).includes(role);

export const isAdminRole = (role?: UserRole | null) => role === 'Admin' || role === 'SuperAdmin';

/** Landing page after login (and for /login when already signed in). */
export function homePathFor(role: UserRole): string {
  switch (role) {
    case 'Teacher': return '/teacher/dashboard';
    case 'Student': return '/student/dashboard';
    // Module 7: invigilators only supervise the exams they are assigned to.
    case 'Invigilator': return '/invigilator/live-monitor';
    default: return '/admin/dashboard';
  }
}

/**
 * Roles an account may be given from the Users page. Only a SuperAdmin manages Admin / SuperAdmin
 * accounts (the API answers anyone else with 403 SUPERADMIN_REQUIRED).
 */
export function assignableRoles(actor?: UserRole | null): UserRole[] {
  const base: UserRole[] = ['Student', 'Teacher', 'Invigilator'];
  return actor === 'SuperAdmin' ? [...base, 'Admin', 'SuperAdmin'] : base;
}

/** True when `actor` may edit / (de)activate / delete / sign out an account with role `target`. */
export const canManageAccount = (actor: UserRole | null | undefined, target: UserRole) =>
  actor === 'SuperAdmin' || (actor === 'Admin' && !isAdminRole(target));
