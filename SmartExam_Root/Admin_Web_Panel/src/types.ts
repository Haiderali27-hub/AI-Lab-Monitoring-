export type SystemRole = 'OrganizationAdmin' | 'Teacher' | 'Student'

export type ApiEnvelope<T> = {
  success: boolean
  code: string
  message: string
  data: T
}

export type UserSummary = {
  id: string
  institutionId: string
  username: string
  email: string
  role: SystemRole
}

export type TokenResponse = {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAtUtc: string
  user: UserSummary
  deviceBound: boolean
}

export type StudentBindingStatus = {
  studentId: string
  username: string
  email: string
  hasBinding: boolean
  boundAtUtc: string | null
  lastSeenAtUtc: string | null
}

export type BatchUploadResult = {
  createdCount: number
  skippedCount: number
  errors: string[]
}

export type LiveRosterItem = {
  studentId: string
  username: string
  status: string
  remainingSeconds: number
  lastHeartbeatUtc: string | null
  isOnline: boolean
}