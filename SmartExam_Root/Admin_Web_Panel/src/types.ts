export type SystemRole = 'SuperAdmin' | 'OrganizationAdmin' | 'Teacher' | 'Student'

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

export type UserListItem = {
  id: string
  username: string
  email: string
  role: SystemRole
  isActive: boolean
  createdAtUtc: string
  departmentId: string | null
  departmentName: string | null
  sectionId: string | null
  sectionName: string | null
}

export type Department = {
  id: string
  name: string
  code: string
  isActive: boolean
  teacherCount: number
  studentCount: number
  sectionCount: number
}

export type AcademicSection = {
  id: string
  departmentId: string
  departmentName: string
  name: string
  code: string
  academicYear: string
  semester: string | null
  isActive: boolean
  studentCount: number
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

export type Lab = {
  id: string
  name: string
  registeredTerminals: number
  isActive: boolean
}

export type Workstation = {
  id: string
  labId: string
  name: string
  ipAddress: string | null
  isActive: boolean
}

export type InstitutionSettings = {
  id: string
  name: string
  contactEmail: string
  logoUrl: string | null
  allowedIpRanges: string | null
  enforceSingleDeviceBinding: boolean
  allowTeacherResetBinding: boolean
  sessionTimeoutMinutes: number
  createdAtUtc: string
}

export type InstitutionListItem = InstitutionSettings

export type CreateInstitutionAdminPayload = {
  username: string
  email: string
  password: string
}

export type UpdateInstitutionPayload = {
  name: string
  contactEmail: string
  logoUrl: string | null
  allowedIpRanges: string | null
  enforceSingleDeviceBinding: boolean
  allowTeacherResetBinding: boolean
  sessionTimeoutMinutes: number
}

export type CreateLabPayload = {
  name: string
  registeredTerminals: number
}

export type CreateWorkstationPayload = {
  name: string
  ipAddress: string | null
}

export type UpdateWorkstationPayload = {
  name: string
  ipAddress: string | null
  isActive: boolean
}

export type ExamReportSummary = {
  examId: string
  examName: string
  startUtc: string
  endUtc: string
  status: string
  candidateCount: number
  attendanceCount: number
  identityVerifiedCount: number
  incidentCount: number
}

export type ExamReportStudent = {
  studentId: string
  username: string
  email: string
  hasBinding: boolean
  attendanceAtUtc: string | null
  identityVerifiedAtUtc: string | null
  lastHeartbeatUtc: string | null
}

export type ExamReportDetail = {
  examId: string
  examName: string
  startUtc: string
  endUtc: string
  status: string
  candidateCount: number
  attendanceCount: number
  identityVerifiedCount: number
  incidentCount: number
  students: ExamReportStudent[]
}

export type ExamCandidate = {
  id: string
  username: string
  email: string
  hasBinding: boolean
  isActive: boolean
}

export type Proctor = {
  id: string
  username: string
  email: string
}

export type ExamSummary = {
  id: string
  name: string
  startUtc: string
  endUtc: string
  status: string
  isActive: boolean
  isCancelled: boolean
  isArchived: boolean
  labName: string | null
  labId: string | null
  proctorName: string | null
  proctorUserId: string | null
  candidateCount: number
  eligibleCount: number
  ineligibleCount: number
  instructions: string | null
}

export type ExamAssignmentDetail = {
  assignmentId: string
  studentId: string
  username: string
  email: string
  isEligible: boolean
  workstationId: string | null
  workstationName: string | null
}

export type ExamAssignmentInput = {
  studentId: string
  isEligible: boolean
  workstationId: string | null
}

export type CreateExamPayload = {
  name: string
  startUtc: string
  endUtc: string
  labId: string | null
  proctorUserId: string | null
  instructions: string | null
  assignments: ExamAssignmentInput[]
}

export type UpdateExamPayload = {
  name: string
  startUtc: string
  endUtc: string
  labId: string | null
  proctorUserId: string | null
  instructions: string | null
  isActive: boolean
}

export type UpdateUserPayload = {
  username: string
  email: string
  isActive: boolean
}

