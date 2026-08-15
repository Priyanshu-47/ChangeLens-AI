import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { authApi } from '../api/endpoints';
import { tokenStore } from '../api/client';
import type { User } from '../api/types';

interface AuthState {
  user: User | null;
  /** True while the initial /auth/me session check is in flight. */
  initializing: boolean;
  login: (email: string, password: string) => Promise<User>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [initializing, setInitializing] = useState(true);

  useEffect(() => {
    let cancelled = false;
    const token = tokenStore.get();
    if (!token) {
      setInitializing(false);
      return;
    }
    authApi
      .me()
      .then((me) => {
        if (!cancelled) {
          setUser(me.user);
        }
      })
      .catch(() => {
        // Expired/invalid token — clear it; the protected route will redirect to login.
        if (!cancelled) {
          tokenStore.clear();
        }
      })
      .finally(() => {
        if (!cancelled) {
          setInitializing(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const auth = await authApi.login(email, password);
    tokenStore.set(auth.accessToken);
    setUser(auth.user);
    return auth.user;
  }, []);

  const logout = useCallback(() => {
    authApi.logout();
    tokenStore.clear();
    setUser(null);
  }, []);

  const value = useMemo<AuthState>(
    () => ({ user, initializing, login, logout }),
    [user, initializing, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return ctx;
}

/** Redirects unauthenticated visitors to /login, preserving the intended destination. */
export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { user, initializing } = useAuth();
  const location = useLocation();

  if (initializing) {
    return <div className="page-center" aria-busy="true">Checking session…</div>;
  }
  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }
  return <>{children}</>;
}
