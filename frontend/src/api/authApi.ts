// Sign-in (SH-S01): our API checks the username and password with Registration
// and gives back our own token. See backend/Controllers/AuthController.cs.
import { ApiError } from './apiClient';

export interface LoginResult {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
  name: string;
  username: string;
  role: string;             // our role, e.g. emergency:Admin
  registrationRole: string; // Registration's name for it, e.g. Manager
}

/** Friendly text for each error the login endpoint can send back. */
const LOGIN_MESSAGES: Record<string, string> = {
  invalid_credentials: 'Wrong username or password.',
  no_emergency_role: "Your city account doesn't have access to Emergency Services.",
  registration_unavailable: "The city login service isn't responding. Try again in a minute.",
  too_many_attempts: 'Too many sign-in attempts. Wait a minute and try again.',
  login_not_configured: "The Emergency API isn't set up for sign-in yet (missing JWT_SIGNING_KEY).",
  validation_failed: 'Enter your username and password.',
  network_error: "Can't reach the Emergency API. Is it running?",
};

export function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return LOGIN_MESSAGES[error.code] ?? error.message;
  return 'Sign-in failed. Try again.';
}

/** POST /api/emergency/v1/auth/login. Throws ApiError on failure. Never sends an old token. */
export async function login(username: string, password: string): Promise<LoginResult> {
  let response: Response;
  try {
    response = await fetch('/api/emergency/v1/auth/login', {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });
  } catch {
    throw new ApiError(0, 'network_error', 'Cannot reach the Emergency API.');
  }

  let json: unknown;
  try {
    json = await response.json();
  } catch {
    json = undefined;
  }

  if (!response.ok) {
    const err = (json ?? {}) as { error?: string; message?: string };
    throw new ApiError(response.status, err.error ?? `http_${response.status}`,
      err.message ?? `Sign-in failed (${response.status}).`);
  }
  return json as LoginResult;
}
