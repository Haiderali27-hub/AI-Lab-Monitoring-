import { createContext, useContext, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import type { AuthUser } from '../types';
import { TOKEN_KEY, REFRESH_TOKEN_KEY, USER_KEY, clearStoredSession } from '../api/client';
import { authApi } from '../api/auth.api';

interface AuthContextType {
  user: AuthUser | null;
  token: string | null;
  login: (token: string, user: AuthUser, refreshToken?: string | null) => void;
  logout: () => void;
  isLoading: boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const t = localStorage.getItem(TOKEN_KEY);
    const u = localStorage.getItem(USER_KEY);
    if (t && u) {
      try {
        const parsedUser = JSON.parse(u);
        const validRoles = ['SuperAdmin', 'Admin', 'Teacher', 'Student'];
        if (parsedUser && typeof parsedUser.role === 'string' && validRoles.includes(parsedUser.role)) {
          setToken(t);
          setUser(parsedUser);
        } else {
          // Outdated or invalid role format in cache (e.g. old numeric role) -> clean it
          clearStoredSession();
        }
      } catch (e) {
        clearStoredSession();
      }
    }
    setIsLoading(false);
  }, []);

  const login = (token: string, user: AuthUser, refreshToken?: string | null) => {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    if (refreshToken) localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    else localStorage.removeItem(REFRESH_TOKEN_KEY);
    setToken(token);
    setUser(user);
  };

  const logout = () => {
    // Revoke the session + refresh-token family server-side (best effort, bounded wait), then clear.
    const finish = () => {
      clearStoredSession();
      setToken(null);
      setUser(null);
      window.location.href = '/login';
    };
    if (localStorage.getItem(TOKEN_KEY)) {
      authApi.logout().catch(() => undefined).finally(finish);
    } else {
      finish();
    }
  };

  return (
    <AuthContext.Provider value={{ user, token, login, logout, isLoading }}>
      {children}
    </AuthContext.Provider>
  );
}

// Use this hook in every page component that needs user info
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthProvider');
  return ctx;
}
