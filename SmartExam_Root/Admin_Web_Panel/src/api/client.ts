import axios, { type InternalAxiosRequestConfig } from 'axios';
import { API_BASE_URL } from '../utils/constants';

export const TOKEN_KEY = 'smartexam_token';
export const REFRESH_TOKEN_KEY = 'smartexam_refresh_token';
export const USER_KEY = 'smartexam_user';

/** Fired on window after a successful refresh; detail = the new access token. */
export const TOKEN_REFRESHED_EVENT = 'smartexam:token-refreshed';

// Refresh this many seconds before the access token's `exp`, so requests never race the expiry.
const PROACTIVE_REFRESH_SECONDS = 60;

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
});

export function clearStoredSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
}

/** Seconds until the JWT's `exp`, or null when it cannot be decoded. */
export function secondsUntilExpiry(jwt: string | null): number | null {
  if (!jwt) return null;
  try {
    const payload = jwt.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const { exp } = JSON.parse(atob(payload.padEnd(payload.length + ((4 - (payload.length % 4)) % 4), '=')));
    return typeof exp === 'number' ? exp - Date.now() / 1000 : null;
  } catch {
    return null;
  }
}

type RefreshResult = { token: string | null; rejected: boolean };

let refreshInFlight: Promise<RefreshResult> | null = null;

async function doRefresh(failedToken: string | null): Promise<RefreshResult> {
  // Another tab (or an earlier caller) may already have rotated the pair: reuse its result rather than
  // replaying the old refresh token, which the server treats as theft and answers by revoking the family.
  const current = localStorage.getItem(TOKEN_KEY);
  if (current && current !== failedToken && (secondsUntilExpiry(current) ?? 0) > PROACTIVE_REFRESH_SECONDS) {
    return { token: current, rejected: false };
  }

  const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
  if (!refreshToken) return { token: null, rejected: true };

  try {
    // Plain axios: the refresh call must not go through this client's own interceptors.
    const res = await axios.post(`${API_BASE_URL}/auth/refresh`, { refreshToken });
    const data = res.data?.data;
    if (!data?.accessToken || !data?.refreshToken) return { token: null, rejected: true };
    localStorage.setItem(TOKEN_KEY, data.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, data.refreshToken);
    window.dispatchEvent(new CustomEvent(TOKEN_REFRESHED_EVENT, { detail: data.accessToken }));
    return { token: data.accessToken, rejected: false };
  } catch (err: any) {
    // Only an explicit 401 ends the session; a network blip / 429 / 5xx keeps it for the next try.
    return { token: null, rejected: err?.response?.status === 401 };
  }
}

/**
 * Single-flight access-token refresh. Concurrent callers in this tab share one request, and the Web
 * Locks API serialises refreshes across tabs (they share localStorage, hence the same refresh token).
 */
export function refreshAccessToken(failedToken: string | null = localStorage.getItem(TOKEN_KEY)): Promise<RefreshResult> {
  if (!refreshInFlight) {
    const run = () => doRefresh(failedToken);
    const locks = (navigator as Navigator & { locks?: LockManager }).locks;
    refreshInFlight = (locks ? locks.request('smartexam-token-refresh', run) : run())
      .finally(() => { refreshInFlight = null; });
  }
  return refreshInFlight;
}

/**
 * The access token to use right now, refreshed first when it is about to expire. For callers outside
 * axios: fetch() downloads and the SignalR accessTokenFactory (so the hub reconnects after expiry).
 */
export async function getFreshAccessToken(): Promise<string> {
  const token = localStorage.getItem(TOKEN_KEY);
  const remaining = secondsUntilExpiry(token);
  if (token && remaining !== null && remaining < PROACTIVE_REFRESH_SECONDS && localStorage.getItem(REFRESH_TOKEN_KEY)) {
    const result = await refreshAccessToken(token);
    if (result.token) return result.token;
  }
  return localStorage.getItem(TOKEN_KEY) ?? '';
}

// Auth endpoints that must never trigger a refresh-and-retry.
const NO_REFRESH = ['/auth/login', '/auth/refresh', '/auth/logout', '/auth/student-login'];

function redirectToLogin() {
  clearStoredSession();
  if (!window.location.pathname.includes('/login')) {
    window.location.href = '/login';
  }
}

// Attach the (proactively refreshed) JWT to every request
apiClient.interceptors.request.use(async (config) => {
  const skip = NO_REFRESH.some(p => (config.url ?? '').includes(p));
  const token = skip ? localStorage.getItem(TOKEN_KEY) : await getFreshAccessToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// On a 401: refresh once (single-flight) and retry; if the session is really over, back to login.
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
    if (error.response?.status !== 401 || !original) return Promise.reject(error);

    const url = original.url ?? '';
    if (NO_REFRESH.some(p => url.includes(p))) {
      if (!url.includes('/auth/login')) redirectToLogin(); // a failed login stays on the login page
      return Promise.reject(error);
    }

    if (!original._retried && localStorage.getItem(REFRESH_TOKEN_KEY)) {
      original._retried = true;
      const failedToken = String(original.headers?.Authorization ?? '').replace(/^Bearer /, '') || null;
      const result = await refreshAccessToken(failedToken);
      if (result.token) {
        original.headers.Authorization = `Bearer ${result.token}`;
        return apiClient(original);
      }
      if (!result.rejected) return Promise.reject(error); // server unreachable: keep the session
    }

    redirectToLogin();
    return Promise.reject(error);
  }
);

export async function unwrap<T>(promise: Promise<{ data: T } | any>): Promise<T> {
  const response = await promise;
  return response.data;
}

export default apiClient;
