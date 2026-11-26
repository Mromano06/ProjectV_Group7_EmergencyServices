import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  apiRequest, getApiToken, getSessionToken, setSessionToken, SESSION_EXPIRED_EVENT,
} from '../api/apiClient';
import { AuthProvider, useAuth } from '../auth/AuthContext';
import AuthGate from '../auth/AuthGate';
import { tokenExpiresAt } from '../auth/token';

// SH-S01: the console asks for a Registration username and password, our API checks them,
// and the token it gives back is used for every call. The API and Registration are faked.

// The real console polls the API; for these tests it just shows its children.
vi.mock('../state/DispatchContext', () => ({
  DispatchProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

/** A token shaped like the one our API makes (the browser never checks the signature). */
function makeToken(claims: Record<string, unknown>): string {
  const part = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${part({ alg: 'HS256', typ: 'JWT' })}.${part(claims)}.sig`;
}
const inAnHour = () => Math.floor(Date.now() / 1000) + 3600;
const ADMIN_TOKEN = () => makeToken({ name: 'Terry Tester', role: 'emergency:Admin', exp: inAnHour() });

function reply(status: number, body: unknown) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json' },
  }));
}

/** Shows who is signed in, with a sign-out button, standing in for the console. */
function Console() {
  const { user, signOut } = useAuth();
  return (
    <div>
      <p>Console for {user.name} ({user.role})</p>
      <button onClick={() => signOut('You signed out.')}>Sign out</button>
    </div>
  );
}

function showApp() {
  render(
    <AuthProvider>
      <AuthGate>
        <Console />
      </AuthGate>
    </AuthProvider>,
  );
}

async function signInAs(username: string, password: string) {
  await userEvent.type(screen.getByLabelText('Username'), username);
  await userEvent.type(screen.getByLabelText('Password'), password);
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

beforeEach(() => {
  setSessionToken(undefined);
  sessionStorage.clear();
});

describe('Login screen (SH-S01)', () => {
  it('shows the login screen, not the console, before anyone signs in', () => {
    showApp();
    expect(screen.getByRole('heading', { name: 'Emergency Services' })).toBeInTheDocument();
    expect(screen.queryByText(/Console for/)).not.toBeInTheDocument();
  });

  it('signs in through our API and opens the console with the right name and role', async () => {
    const token = ADMIN_TOKEN();
    const fetchMock = vi.fn(() => reply(200, {
      accessToken: token, tokenType: 'Bearer', expiresAtUtc: '2030-01-01T00:00:00Z',
      name: 'Terry Tester', username: 'test.manager', role: 'emergency:Admin', registrationRole: 'Manager',
    }));
    vi.stubGlobal('fetch', fetchMock);
    showApp();

    await signInAs('  test.manager ', 'pw-manager');

    expect(await screen.findByText('Console for Terry Tester (Admin)')).toBeInTheDocument();
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/emergency/v1/auth/login');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ username: 'test.manager', password: 'pw-manager' });
    expect(getApiToken()).toBe(token);
    expect(sessionStorage.getItem('emergency.accessToken')).toBe(token);
  });

  it('says so when the username or password is wrong, and clears the password', async () => {
    vi.stubGlobal('fetch', vi.fn(() => reply(401, { error: 'invalid_credentials', message: 'x', traceId: 't' })));
    showApp();

    await signInAs('test.manager', 'nope');

    expect(await screen.findByRole('alert')).toHaveTextContent('Wrong username or password.');
    expect(screen.getByLabelText('Password')).toHaveValue('');
    expect(screen.getByLabelText('Username')).toHaveValue('test.manager');
  });

  it('explains when Registration is down', async () => {
    vi.stubGlobal('fetch', vi.fn(() => reply(503, { error: 'registration_unavailable', message: 'x', traceId: 't' })));
    showApp();

    await signInAs('test.manager', 'pw-manager');

    expect(await screen.findByRole('alert')).toHaveTextContent("city login service isn't responding");
  });

  it('explains when the account has no Emergency access', async () => {
    vi.stubGlobal('fetch', vi.fn(() => reply(403, { error: 'no_emergency_role', message: 'x', traceId: 't' })));
    showApp();

    await signInAs('resident', 'pw');

    expect(await screen.findByRole('alert')).toHaveTextContent("doesn't have access to Emergency Services");
  });

  it('asks for both fields without calling the API', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    showApp();

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Enter your username and password.');
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('Staying signed in, and signing out', () => {
  it('keeps you signed in after a page refresh (token saved for this tab)', () => {
    setSessionToken(ADMIN_TOKEN());
    showApp();
    expect(screen.getByText('Console for Terry Tester (Admin)')).toBeInTheDocument();
  });

  it('ignores a saved token that has already expired', () => {
    setSessionToken(makeToken({ name: 'Old', role: 'emergency:Admin', exp: Math.floor(Date.now() / 1000) - 60 }));
    showApp();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(getSessionToken()).toBeUndefined();
  });

  it('signing out forgets the token and goes back to the login screen', async () => {
    setSessionToken(ADMIN_TOKEN());
    showApp();

    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(screen.getByRole('status')).toHaveTextContent('You signed out.');
    expect(getSessionToken()).toBeUndefined();
    expect(sessionStorage.getItem('emergency.accessToken')).toBeNull();
  });

  it('goes back to the login screen when the API says the session expired (401)', async () => {
    const token = ADMIN_TOKEN();
    setSessionToken(token);
    showApp();
    vi.stubGlobal('fetch', vi.fn(() => reply(401, {})));

    await act(async () => {
      await expect(apiRequest('GET', '/api/emergency/v1/units')).rejects.toMatchObject({ status: 401 });
    });

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Your session expired'));
    expect(getSessionToken()).toBeUndefined();
  });

  it('does not sign out when a 401 comes back for some other token', async () => {
    const listener = vi.fn();
    window.addEventListener(SESSION_EXPIRED_EVENT, listener);
    vi.stubGlobal('fetch', vi.fn(() => reply(401, {})));

    await expect(apiRequest('GET', '/api/emergency/v1/units')).rejects.toMatchObject({ status: 401 });

    expect(listener).not.toHaveBeenCalled();
    window.removeEventListener(SESSION_EXPIRED_EVENT, listener);
  });
});

describe('tokenExpiresAt', () => {
  it('reads "exp" as milliseconds', () => {
    expect(tokenExpiresAt(makeToken({ exp: 1_900_000_000 }))).toBe(1_900_000_000_000);
  });

  it('is undefined for no token, no exp, or junk', () => {
    expect(tokenExpiresAt(undefined)).toBeUndefined();
    expect(tokenExpiresAt(makeToken({ name: 'x' }))).toBeUndefined();
    expect(tokenExpiresAt('not-a-token')).toBeUndefined();
  });
});
