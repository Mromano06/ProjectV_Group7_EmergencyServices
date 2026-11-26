/**
 * @file AuthContext.tsx
 * @brief Provides the current user's identity and role to frontend components.
 */

import { createContext, useContext, useState, type ReactNode } from 'react';
import { getApiToken } from '../api/apiClient';
import type { Role } from '../types/models';
import { userFromToken, type AuthUser } from './token';

interface AuthContextValue {
  user: AuthUser;
  setRole: (role: Role) => void; // for tests
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children, initialRole }: { children: ReactNode; initialRole?: Role }) {
  const [user, setUser] = useState<AuthUser>(() => {
    const fromToken = userFromToken(getApiToken());
    return initialRole ? { ...fromToken, role: initialRole } : fromToken;
  });
  const setRole = (role: Role) => setUser((u) => ({ ...u, role }));

  return (
    <AuthContext.Provider value={{ user, setRole }}>
      {children}
    </AuthContext.Provider>
  );
}
/** Returns the auth context; this hook must be used inside AuthProvider. */
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
