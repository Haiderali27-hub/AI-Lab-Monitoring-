import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import * as authApi from '../api/auth'
import type { TokenResponse, UserSummary } from '../types'

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

type AuthContextValue = {
  user: UserSummary | null
  accessToken: string | null
  isAuthenticated: boolean
  isPlatformBootstrapped: boolean | null
  checkBootstrapStatus: () => Promise<void>
  setup: (payload: BootstrapPayload) => Promise<UserSummary>
  login: (payload: LoginPayload) => Promise<UserSummary>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

const TOKEN_KEY = 'smartexam_access_token'
const REFRESH_TOKEN_KEY = 'smartexam_refresh_token'
const USER_KEY = 'smartexam_user'

function getInitialUser(): UserSummary | null {
  const userRaw = localStorage.getItem(USER_KEY)
  if (!userRaw) {
    return null
  }

  try {
    return JSON.parse(userRaw) as UserSummary
  } catch {
    return null
  }
}

function setSession(tokenResponse: TokenResponse): void {
  localStorage.setItem(TOKEN_KEY, tokenResponse.accessToken)
  localStorage.setItem(REFRESH_TOKEN_KEY, tokenResponse.refreshToken)
  localStorage.setItem(USER_KEY, JSON.stringify(tokenResponse.user))
}

function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(REFRESH_TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [accessToken, setAccessToken] = useState<string | null>(localStorage.getItem(TOKEN_KEY))
  const [user, setUser] = useState<UserSummary | null>(getInitialUser)
  const [isPlatformBootstrapped, setIsPlatformBootstrapped] = useState<boolean | null>(null)

  const checkBootstrapStatus = useCallback(async () => {
    try {
      const data = await authApi.getBootstrapStatus()
      setIsPlatformBootstrapped(data.isBootstrapped)
    } catch {
      setIsPlatformBootstrapped(false)
    }
  }, [])

  useEffect(() => {
    checkBootstrapStatus()
  }, [checkBootstrapStatus])

  const applySession = useCallback((tokenResponse: TokenResponse) => {
    setSession(tokenResponse)
    setAccessToken(tokenResponse.accessToken)
    setUser(tokenResponse.user)
  }, [])

  const setup = useCallback(
    async (payload: BootstrapPayload) => {
      const tokenResponse = await authApi.registerOrganization(payload)
      applySession(tokenResponse)
      setIsPlatformBootstrapped(true)
      return tokenResponse.user
    },
    [applySession],
  )

  const login = useCallback(
    async (payload: LoginPayload) => {
      const tokenResponse = await authApi.loginUser(payload)
      applySession(tokenResponse)
      return tokenResponse.user
    },
    [applySession],
  )

  const logout = useCallback(async () => {
    try {
      if (accessToken) {
        await authApi.logoutUser()
      }
    } finally {
      clearSession()
      setAccessToken(null)
      setUser(null)
    }
  }, [accessToken])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      accessToken,
      isAuthenticated: Boolean(accessToken && user),
      isPlatformBootstrapped,
      checkBootstrapStatus,
      setup,
      login,
      logout,
    }),
    [accessToken, checkBootstrapStatus, isPlatformBootstrapped, login, logout, setup, user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider')
  }

  return context
}
