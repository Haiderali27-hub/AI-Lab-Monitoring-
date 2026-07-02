import apiClient from './client';
import type { LoginRequest, LoginResponse, AuthUser } from '../types';

export const authApi = {
  login: (data: LoginRequest) =>
    apiClient.post<LoginResponse>('/auth/login', data).then(r => r.data),

  logout: () =>
    apiClient.post('/auth/logout'),

  me: () =>
    apiClient.get<AuthUser>('/auth/me').then(r => r.data),

  changePassword: (currentPassword: string, newPassword: string) =>
    apiClient.post('/auth/change-password', { currentPassword, newPassword }).then(r => r.data),
};
