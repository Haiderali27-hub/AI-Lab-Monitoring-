import axios from 'axios'
import type { ApiEnvelope } from '../types'

const baseURL = import.meta.env.VITE_API_BASE_URL ?? 'https://localhost:7068'

export const apiClient = axios.create({
  baseURL,
  timeout: 15000,
  headers: {
    'Content-Type': 'application/json',
  },
})

apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('smartexam_access_token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

function extractMessage(error: unknown, fallback: string): string {
  if (!axios.isAxiosError(error)) {
    return fallback
  }

  const responseData = error.response?.data as ApiEnvelope<unknown> | undefined
  return responseData?.message ?? error.message ?? fallback
}

export async function unwrap<T>(request: Promise<{ data: ApiEnvelope<T> }>): Promise<T> {
  try {
    const response = await request
    if (!response.data.success) {
      throw new Error(response.data.message || 'Request failed')
    }
    return response.data.data
  } catch (error) {
    throw new Error(extractMessage(error, 'Network request failed'))
  }
}