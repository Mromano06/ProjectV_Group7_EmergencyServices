import { createContext, useContext, useState, type ReactNode } from 'react';
import type { Role } from '../types/models';

interface AuthUser {
  name: string;
  role: Role;
}

interface AuthContextValue {
  user: AuthUser;
  setRole: (role: Role) => void; // mock only, for testing
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [role, setRole] = useState<Role>('Dispatcher');
  const user: AuthUser = { name: 'Test User', role };

  return (
    <AuthContext.Provider value={{ user, setRole }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}