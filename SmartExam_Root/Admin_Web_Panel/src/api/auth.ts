import type { TokenResponse } from '../types'
import { apiClient, unwrap } from './client'

type BootstrapPayload = {
  institutionName: string
  username: string
  email: string
  password: string
}

type LoginPayload = {
  usernameOrEmail: string
  password: string
}

export async function bootstrapAdmin(payload: BootstrapPayload): Promise<TokenResponse> {
  return unwrap<TokenResponse>(apiClient.post('/api/auth/bootstrap-admin', payload))
}

export async function loginUser(payload: LoginPayload): Promise<TokenResponse> {
  return unwrap<TokenResponse>(apiClient.post('/api/auth/login', payload))
}

export async function logoutUser(): Promise<void> {
  await unwrap<boolean>(apiClient.post('/api/auth/logout', {}))
}