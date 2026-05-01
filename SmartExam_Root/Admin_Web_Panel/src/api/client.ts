import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import type { ApiEnvelope, TokenResponse } from '../types'

const baseURL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5067'
const TOKEN_KEY = 'smartexam_access_token'
const REFRESH_TOKEN_KEY = 'smartexam_refresh_token'
const USER_KEY = 'smartexam_user'

type RetriableRequestConfig = InternalAxiosRequestConfig & {
  _retry?: boolean
}

export const apiClient = axios.create({
  baseURL,
  timeout: 15000,
  headers: {
    'Content-Type': 'application/json',
  },
})

apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_KEY)
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiEnvelope<unknown>>) => {
    const originalRequest = error.config as RetriableRequestConfig | undefined
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY)

    if (error.response?.status !== 401 || !originalRequest || originalRequest._retry || !refreshToken) {
      throw error
    }

    originalRequest._retry = true

    try {
      const response = await axios.post<ApiEnvelope<TokenResponse>>(
        `${baseURL}/api/auth/refresh`,
        { refreshToken },
        { headers: { 'Content-Type': 'application/json' } },
      )

      if (!response.data.success) {
        throw error
      }

      const tokenResponse = response.data.data
      localStorage.setItem(TOKEN_KEY, tokenResponse.accessToken)
      localStorage.setItem(REFRESH_TOKEN_KEY, tokenResponse.refreshToken)
      localStorage.setItem(USER_KEY, JSON.stringify(tokenResponse.user))
      originalRequest.headers.Authorization = `Bearer ${tokenResponse.accessToken}`
      return apiClient(originalRequest)
    } catch {
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(REFRESH_TOKEN_KEY)
      localStorage.removeItem(USER_KEY)
      throw error
    }
  },
)

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
    throw new Error(extractMessage(error, 'Network request failed'), { cause: error })
  }
}
