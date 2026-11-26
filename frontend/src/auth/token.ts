/**
 * @file token.ts
 * @brief Reads user name and emergency role claims from the configured JWT.
 *
 * The API remains responsible for verifying and authorizing every request.
 */

import type { Role } from '../types/models';

export interface AuthUser {
  name: string;
  role: Role;
}

// Highest first: a token with several emergency roles gets the strongest one.
const ROLE_ORDER: Role[] = ['Admin', 'Dispatcher', 'Responder', 'Viewer'];
const ROLE_CLAIMS = ['role', 'roles', 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];

// Registration's plain role names, mapped the same way as the API (backend appsettings.json "RoleMapping").
const PLAIN_ROLES: Record<string, Role> = {
  manager: 'Admin',
  dispatcher: 'Dispatcher',
  user: 'Viewer',
};

/**
 * Reads the user's name and emergency role from the JWT: "emergency:Dispatcher" (SH-S02) or
 * Registration's plain names (Manager, Dispatcher, User).
 * Only used to decide what to SHOW (SH-S05) – the API still checks every request itself.
 * No token or no emergency role -> Viewer (read-only).
 */
export function userFromToken(token: string | undefined): AuthUser {
  const fallback: AuthUser = { name: 'Guest', role: 'Viewer' };
  if (!token) return fallback;
  try {
    const claims = readClaims(token);

    const values = ROLE_CLAIMS.flatMap((key) => {
      const v = claims[key];
      return Array.isArray(v) ? v : v === undefined ? [] : [v];
    }).map(String);
    const roles = values.flatMap((r) => {
      const lower = r.trim().toLowerCase();
      if (lower.startsWith('emergency:')) return [r.trim().slice('emergency:'.length)];
      const mapped = PLAIN_ROLES[lower];
      return mapped ? [mapped] : [];
    });
    const role = ROLE_ORDER.find((r) => roles.some((x) => x.toLowerCase() === r.toLowerCase())) ?? 'Viewer';

    const name = String(claims.name ?? claims.unique_name ?? claims.sub ?? 'User');
    return { name, role };
  } catch {
    return fallback;
  }
}

/** The middle part of a JWT as an object. Throws if it isn't a JWT. */
function readClaims(token: string): Record<string, unknown> {
  const part = token.split('.')[1];
  const base64 = part.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(part.length / 4) * 4, '=');
  // atob gives bytes; decode them as UTF-8 so names like "Zoë" come out right.
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
}

/** When the token runs out (ms since 1970, like Date.now()), or undefined if it has no "exp". */
export function tokenExpiresAt(token: string | undefined): number | undefined {
  if (!token) return undefined;
  try {
    const exp = readClaims(token).exp;
    return typeof exp === 'number' ? exp * 1000 : undefined;
  } catch {
    return undefined;
  }
}
