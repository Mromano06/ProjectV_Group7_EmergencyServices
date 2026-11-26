import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import {
  getDevToken, getSessionToken, setSessionToken, SESSION_EXPIRED_EVENT,
} from '../api/apiClient';
import type { Role } from '../types/models';
import { tokenExpiresAt, userFromToken, type AuthUser } from './token';

interface AuthContextValue {
  user: AuthUser;
  /** True once there is a token to use (signed in, or the VITE_API_TOKEN backup). */
  signedIn: boolean;
  /** True when using VITE_API_TOKEN instead of a real sign-in (no sign-out button then). */
  usingDevToken: boolean;
  /** A message for the login screen, e.g. "Your session expired". */
  notice: string | undefined;
  signIn: (token: string) => void;
  signOut: (notice?: string) => void;
  setRole: (role: Role) => void; // for tests
}

const AuthContext = createContext<AuthContextValue | null>(null);

const EXPIRED = 'Your session expired. Please sign in again.';

/** A token that is still usable (not past its expiry time). */
function usable(token: string | undefined): string | undefined {
  if (!token) return undefined;
  const ends = tokenExpiresAt(token);
  return ends !== undefined && ends <= Date.now() ? undefined : token;
}

export function AuthProvider({ children, initialRole }: { children: ReactNode; initialRole?: Role }) {
  // Tests pass initialRole to skip signing in.
  const [token, setToken] = useState<string | undefined>(() => {
    const saved = usable(getSessionToken());
    if (!saved) setSessionToken(undefined); // throw away an expired one from last time
    return saved;
  });
  const [roleOverride, setRoleOverride] = useState<Role | undefined>(initialRole);
  const [notice, setNotice] = useState<string | undefined>(undefined);

  const devToken = token ? undefined : getDevToken();
  const activeToken = token ?? devToken;
  const fromToken = userFromToken(activeToken);
  const user = roleOverride ? { ...fromToken, role: roleOverride } : fromToken;

  const signIn = useCallback((newToken: string) => {
    setSessionToken(newToken);
    setToken(newToken);
    setNotice(undefined);
  }, []);

  const signOut = useCallback((message?: string) => {
    setSessionToken(undefined);
    setToken(undefined);
    setRoleOverride(undefined);
    setNotice(message);
  }, []);

  // The API said 401 to our token (apiClient already forgot it).
  useEffect(() => {
    const onExpired = () => signOut(EXPIRED);
    window.addEventListener(SESSION_EXPIRED_EVENT, onExpired);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, onExpired);
  }, [signOut]);

  // Sign out by ourselves when the token's time is up, instead of waiting for errors.
  useEffect(() => {
    const ends = token ? tokenExpiresAt(token) : undefined;
    if (ends === undefined) return;
    const timer = window.setTimeout(() => signOut(EXPIRED), Math.max(0, ends - Date.now()));
    return () => window.clearTimeout(timer);
  }, [token, signOut]);

  const value: AuthContextValue = {
    user,
    signedIn: Boolean(activeToken) || (initialRole !== undefined && roleOverride !== undefined),
    usingDevToken: !token && Boolean(devToken),
    notice,
    signIn,
    signOut,
    setRole: (role: Role) => setRoleOverride(role),
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
/** Returns the auth context; this hook must be used inside AuthProvider. */
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
