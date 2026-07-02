import apiClient from './client';
import type { User, CreateUserRequest, UserRole } from '../types';

export interface UpdateUserRequest {
  name: string;
  email: string;
  role: UserRole;
  password?: string;
}

export interface ImportUserRow {
  name: string;
  email: string;
  password: string;
  role: UserRole;
}

export const usersApi = {
  getAll: (role?: UserRole) =>
    apiClient.get<User[]>('/users', { params: role ? { role } : undefined })
      .then(r => r.data),

  create: (data: CreateUserRequest) =>
    apiClient.post<User>('/users', data).then(r => r.data),

  update: (id: string, data: UpdateUserRequest) =>
    apiClient.put(`/users/${id}`, data).then(r => r.data),

  remove: (id: string) =>
    apiClient.delete(`/users/${id}`).then(r => r.data),

  deactivate: (id: string) =>
    apiClient.patch(`/users/${id}/deactivate`),

  activate: (id: string) =>
    apiClient.patch(`/users/${id}/activate`),

  importUsers: (users: ImportUserRow[]) =>
    apiClient.post('/users/import', { users }).then(r => r.data),

  resetDeviceBinding: (id: string) =>
    apiClient.delete(`/users/${id}/device-binding`),

  forceLogout: (id: string) =>
    apiClient.post(`/users/${id}/force-logout`),
};
