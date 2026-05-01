import type { CreateInstitutionAdminPayload, InstitutionListItem, UpdateInstitutionPayload } from '../types'
import { apiClient, unwrap } from './client'

export async function getInstitutions(): Promise<InstitutionListItem[]> {
  return unwrap<InstitutionListItem[]>(apiClient.get('/api/super-admin/institutions'))
}

export async function createInstitution(payload: UpdateInstitutionPayload): Promise<InstitutionListItem> {
  return unwrap<InstitutionListItem>(apiClient.post('/api/super-admin/institutions', payload))
}

export async function createInstitutionAdmin(
  institutionId: string,
  payload: CreateInstitutionAdminPayload,
): Promise<{ id: string; username: string; email: string }> {
  return unwrap<{ id: string; username: string; email: string }>(
    apiClient.post(`/api/super-admin/institutions/${institutionId}/admins`, payload),
  )
}
