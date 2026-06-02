import apiClient from './client';
import type { User, CreateUserRequest, UserRole } from '../types';

export const usersApi = {
  getAll: (role?: UserRole) =>
    apiClient.get<User[]>('/users', { params: role ? { role } : undefined })
      .then(r => r.data),

  create: (data: CreateUserRequest) =>
    apiClient.post<User>('/users', data).then(r => r.data),

  deactivate: (id: string) =>
    apiClient.patch(`/users/${id}/deactivate`),

  resetDeviceBinding: (id: string) =>
    apiClient.delete(`/users/${id}/device-binding`),

  forceLogout: (id: string) =>
    apiClient.post(`/users/${id}/force-logout`),
};
