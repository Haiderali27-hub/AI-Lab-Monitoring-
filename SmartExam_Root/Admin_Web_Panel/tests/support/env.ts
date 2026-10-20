// Shared constants for the headless API + browser suites.
// The backend is started by playwright.config.ts in E2E in-memory mode on :5050
// (the web panel's API base is hardcoded to this port in src/utils/constants.ts).

export const API_URL = process.env.SMARTEXAM_API_URL ?? 'http://localhost:5050';

// Seed accounts (DbSeeder — Development / E2E only)
export const SEED = {
  admin:   { email: 'admin@smartexam.com',   password: 'Admin@123' },
  teacher: { email: 'teacher@smartexam.com', password: 'Teacher@123' },
  ali:     { email: 'ali@smartexam.com',     password: 'Student@123', name: 'Ali Hassan' },
  sara:    { email: 'sara@smartexam.com',    password: 'Student@123', name: 'Sara Khan' },
} as const;

/** Short unique tag so every test can create its own users/exams without collisions. */
export function uniqueTag(prefix = 't'): string {
  return `${prefix}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

export const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
