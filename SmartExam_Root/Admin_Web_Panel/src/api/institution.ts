import type { CreateLabPayload, InstitutionSettings, Lab, UpdateInstitutionPayload } from '../types'
import { apiClient, unwrap } from './client'

export async function getLabs(): Promise<Lab[]> {
  return unwrap<Lab[]>(apiClient.get('/api/institution/labs'))
}

export async function createLab(payload: CreateLabPayload): Promise<Lab> {
  return unwrap<Lab>(apiClient.post('/api/institution/labs', payload))
}

export async function deleteLab(labId: string): Promise<void> {
  await unwrap<boolean>(apiClient.delete(`/api/institution/labs/${labId}`))
}

export async function getInstitutionSettings(): Promise<InstitutionSettings> {
  return unwrap<InstitutionSettings>(apiClient.get('/api/institution/settings'))
}

export async function updateInstitutionSettings(payload: UpdateInstitutionPayload): Promise<InstitutionSettings> {
  return unwrap<InstitutionSettings>(apiClient.put('/api/institution/settings', payload))
}
