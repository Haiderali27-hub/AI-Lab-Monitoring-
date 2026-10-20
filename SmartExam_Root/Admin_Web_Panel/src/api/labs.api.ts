import apiClient from './client';
import type { LabInfo } from '../types';

export const labsApi = {
  getAll: () =>
    apiClient.get<LabInfo[]>('/labs').then(r => r.data),

  createLab: (name: string, location: string) =>
    apiClient.post('/labs', { name, location }).then(r => r.data),

  createWorkstation: (labId: string, machineNumber: string, ipAddress: string, hostName?: string) =>
    apiClient.post(`/labs/${labId}/workstations`, { machineNumber, ipAddress, hostName: hostName || null }).then(r => r.data),

  updateWorkstation: (labId: string, workstationId: string, machineNumber: string, ipAddress: string, hostName?: string) =>
    apiClient.put(`/labs/${labId}/workstations/${workstationId}`, { machineNumber, ipAddress, hostName: hostName || null }).then(r => r.data),

  deleteWorkstation: (labId: string, workstationId: string) =>
    apiClient.delete(`/labs/${labId}/workstations/${workstationId}`).then(r => r.data),

  /** PCs students logged in from that no workstation covers (auto-registration is off by default). */
  unregisteredMachines: () =>
    apiClient.get<{ machineName: string; lastStudent: string; lastSeenAt: string }[]>('/labs/unregistered-machines').then(r => r.data),
};
