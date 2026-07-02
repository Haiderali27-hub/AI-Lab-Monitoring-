import { createContext, useContext, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import type { AuthUser } from '../types';

interface AuthContextType {
  user: AuthUser | null;
  token: string | null;
  login: (token: string, user: AuthUser) => void;
  logout: () => void;
  isLoading: boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const t = localStorage.getItem('smartexam_token');
    const u = localStorage.getItem('smartexam_user');
    if (t && u) {
      try {
        const parsedUser = JSON.parse(u);
        const validRoles = ['SuperAdmin', 'Admin', 'Teacher', 'Student'];
        if (parsedUser && typeof parsedUser.role === 'string' && validRoles.includes(parsedUser.role)) {
          setToken(t);
          setUser(parsedUser);
        } else {
          // Outdated or invalid role format in cache (e.g. old numeric role) -> clean it
          localStorage.removeItem('smartexam_token');
          localStorage.removeItem('smartexam_user');
        }
      } catch (e) {
        localStorage.removeItem('smartexam_token');
        localStorage.removeItem('smartexam_user');
      }
    }
    setIsLoading(false);
  }, []);

  const login = (token: string, user: AuthUser) => {
    localStorage.setItem('smartexam_token', token);
    localStorage.setItem('smartexam_user', JSON.stringify(user));
    setToken(token);
    setUser(user);
  };

  const logout = () => {
    localStorage.removeItem('smartexam_token');
    localStorage.removeItem('smartexam_user');
    setToken(null);
    setUser(null);
    window.location.href = '/login';
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
