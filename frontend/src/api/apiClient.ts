/**
 * @file apiClient.ts
 * @brief Provides shared HTTP requests, bearer-token handling, and API errors.
 */

// Shared API client: every call goes to our own API (SH-U02) with the JWT attached.
// The token comes from VITE_API_TOKEN in frontend/.env until Registration's login exists.

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

export function getApiToken(): string | undefined {
  const token = import.meta.env.VITE_API_TOKEN as string | undefined;
  return token && token.trim() !== '' ? token.trim() : undefined;
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH';

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
    const err = (json ?? {}) as { error?: string; message?: string };
    const fallback =
      response.status === 401 ? 'Not logged in (check VITE_API_TOKEN).' :
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
