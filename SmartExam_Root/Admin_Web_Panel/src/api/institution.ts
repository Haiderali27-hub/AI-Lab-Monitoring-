import type {
  CreateLabPayload,
  CreateWorkstationPayload,
  InstitutionSettings,
  Lab,
  UpdateInstitutionPayload,
  UpdateWorkstationPayload,
  Workstation,
} from '../types'
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

export async function getWorkstations(labId: string): Promise<Workstation[]> {
  return unwrap<Workstation[]>(apiClient.get(`/api/institution/labs/${labId}/workstations`))
}

export async function createWorkstation(labId: string, payload: CreateWorkstationPayload): Promise<Workstation> {
  return unwrap<Workstation>(apiClient.post(`/api/institution/labs/${labId}/workstations`, payload))
}

export async function updateWorkstation(
  labId: string,
  workstationId: string,
  payload: UpdateWorkstationPayload,
): Promise<Workstation> {
  return unwrap<Workstation>(apiClient.put(`/api/institution/labs/${labId}/workstations/${workstationId}`, payload))
}

export async function deleteWorkstation(labId: string, workstationId: string): Promise<void> {
  await unwrap<boolean>(apiClient.delete(`/api/institution/labs/${labId}/workstations/${workstationId}`))
}

export async function getInstitutionSettings(): Promise<InstitutionSettings> {
  return unwrap<InstitutionSettings>(apiClient.get('/api/institution/settings'))
}

export async function updateInstitutionSettings(payload: UpdateInstitutionPayload): Promise<InstitutionSettings> {
  return unwrap<InstitutionSettings>(apiClient.put('/api/institution/settings', payload))
}
