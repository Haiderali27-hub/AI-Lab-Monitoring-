import apiClient from './client';
import type { LabInfo } from '../types';

export const labsApi = {
  getAll: () =>
    apiClient.get<LabInfo[]>('/labs').then(r => r.data),

  createLab: (name: string, location: string) =>
    apiClient.post('/labs', { name, location }).then(r => r.data),

  createWorkstation: (labId: string, machineNumber: string, ipAddress: string) =>
    apiClient.post(`/labs/${labId}/workstations`, { machineNumber, ipAddress }).then(r => r.data),
};
