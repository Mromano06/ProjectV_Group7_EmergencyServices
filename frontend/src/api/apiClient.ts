/**
 * @file apiClient.ts
 * @brief Provides shared HTTP requests, bearer-token handling, and API errors.
 */

// Shared API client: every call goes to our own API (SH-U02) with the JWT attached.
// The token comes from signing in (LoginScreen -> POST /auth/login, SH-S01). It is kept in
// sessionStorage, so it survives a page refresh and is gone when the tab is closed.
// VITE_API_TOKEN in frontend/.env is only a backup for when Registration's login is down.

/** An error response from the API, in the shared { error, message, traceId } shape (SH-I03). */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
}

const SESSION_KEY = 'emergency.accessToken';

/** Fired on window when a signed-in session stops working (the API answered 401). */
export const SESSION_EXPIRED_EVENT = 'emergency:session-expired';

function readStoredToken(): string | undefined {
  try {
    return sessionStorage.getItem(SESSION_KEY) ?? undefined;
  } catch {
    return undefined; // storage blocked: the user just signs in again after a refresh
  }
}

let sessionToken: string | undefined = readStoredToken();

/** The token from signing in, if any. */
export function getSessionToken(): string | undefined {
  return sessionToken;
}

/** Saves (or with no token, forgets) the signed-in token. */
export function setSessionToken(token: string | undefined): void {
  sessionToken = token && token.trim() !== '' ? token.trim() : undefined;
  try {
    if (sessionToken) sessionStorage.setItem(SESSION_KEY, sessionToken);
    else sessionStorage.removeItem(SESSION_KEY);
  } catch {
    // storage blocked: the token still works until the page is refreshed
  }
}

/** Backup token from frontend/.env (VITE_API_TOKEN) – skips the login screen. */
export function getDevToken(): string | undefined {
  const token = import.meta.env.VITE_API_TOKEN as string | undefined;
  return token && token.trim() !== '' ? token.trim() : undefined;
}

/** The token sent with every call: the signed-in one first, then the .env backup. */
export function getApiToken(): string | undefined {
  return sessionToken ?? getDevToken();
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

/** Calls the API and returns the JSON body (or undefined for an empty body). Throws ApiError on 4xx/5xx. */
export async function apiRequest<T>(method: Method, url: string, body?: unknown): Promise<T> {
  const token = getApiToken();
  let response: Response;
  try {
    response = await fetch(url, {
      method,
      headers: {
        Accept: 'application/json',
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(0, 'network_error', 'Cannot reach the Emergency API.');
  }

  const text = await response.text();
  let json: unknown = undefined;
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = undefined;
    }
  }

  if (!response.ok) {
    // A signed-in session that the API no longer accepts (expired or the key changed): sign out.
    if (response.status === 401 && token && token === sessionToken) {
      setSessionToken(undefined);
      window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
    }
    const err = (json ?? {}) as { error?: string; message?: string };
    const fallback =
      response.status === 401 ? 'Not signed in, or your session expired.' :
      response.status === 403 ? 'Your role is not allowed to do that.' :
      `Request failed: ${response.status}`;
    throw new ApiError(response.status, err.error ?? `http_${response.status}`, err.message ?? fallback);
  }
  return json as T;
}

/** Kept for older code: GET some JSON. */
export function getApiData<T>(url: string): Promise<T> {
  return apiRequest<T>('GET', url);
}