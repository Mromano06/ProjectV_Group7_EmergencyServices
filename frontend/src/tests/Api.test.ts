/**
 * @file Api.test.ts
 * @brief Tests API request handling, DTO conversion, and JWT role parsing.
 */

import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, apiRequest } from '../api/apiClient';
import { requiredUnitTypeFor, toHospital, toNotice, toUnit } from '../api/emergencyApi';
import { userFromToken } from '../auth/token';

/** Builds an unsigned JWT with the given claims (the console only reads them; the API verifies). */
function jwt(claims: Record<string, unknown>) {
  const enc = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${enc({ alg: 'HS256' })}.${enc(claims)}.signature`;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe('apiRequest', () => {
  it('sends JSON with the token and returns the body', async () => {
    vi.stubEnv('VITE_API_TOKEN', 'abc.def.ghi');
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: '1' }), { status: 201 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await apiRequest<{ id: string }>('POST', '/api/emergency/v1/incidents', { a: 1 });

    expect(result).toEqual({ id: '1' });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/emergency/v1/incidents');
    expect(init.method).toBe('POST');
    expect(init.headers.Authorization).toBe('Bearer abc.def.ghi');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(init.body).toBe('{"a":1}');
  });

  it('turns the shared error shape into an ApiError (SH-I03)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ error: 'unit_unavailable', message: 'Unit is busy.', traceId: 't' }), { status: 409 })));

    const error = (await apiRequest('POST', '/x').catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(409);
    expect(error.code).toBe('unit_unavailable');
    expect(error.message).toBe('Unit is busy.');
  });

  it('reports a network failure as status 0', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = (await apiRequest('GET', '/x').catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(0);
  });

  it('accepts an empty response body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    await expect(apiRequest('PATCH', '/x', {})).resolves.toBeUndefined();
  });
});

describe('converters', () => {
  it('turns an incident into a call card', () => {
    const notice = toNotice({
      id: 'i1', type: 'Medical', severity: 2, status: 'Reported', latitude: 43.4, longitude: -80.3,
      reportedAt: '2026-11-25T15:00:00', address: '55 Oak Ave', description: 'Cardiac arrest',
    });
    expect(notice).toMatchObject({
      id: 'i1', title: 'Cardiac arrest', incidentType: 'Medical', severity: 2,
      locationLabel: '55 Oak Ave', requiredUnitType: 'Ambulance',
    });
    expect(notice.createdAt).toBe(Date.UTC(2026, 10, 25, 15, 0, 0)); // read as UTC (SH-I04)
  });

  it('falls back to coordinates and a generic title', () => {
    const notice = toNotice({
      id: 'i2', type: 'Fire', severity: 1, status: 'Reported', latitude: 43.4, longitude: -80.3,
      reportedAt: '2026-11-25T15:00:00Z', address: null, description: null,
    });
    expect(notice.title).toBe('Fire call');
    expect(notice.locationLabel).toBe('43.4000, -80.3000');
  });

  it('picks the unit type each call needs', () => {
    expect(requiredUnitTypeFor('Fire')).toBe('Engine');
    expect(requiredUnitTypeFor('Medical')).toBe('Ambulance');
    expect(requiredUnitTypeFor('Rescue')).toBe('Ladder');
    expect(requiredUnitTypeFor('Hazmat')).toBe('Engine');
  });

  it('names a unit\'s home station and marks Out of Service units inactive', () => {
    const names = new Map([['s1', 'Station 1']]);
    expect(toUnit({ id: 'u', callSign: 'Engine 1', type: 'Engine', stationId: 's1', status: 'Available' }, names))
      .toMatchObject({ homeStation: 'Station 1', active: true });
    expect(toUnit({ id: 'u', callSign: 'X', type: 'Engine', stationId: 'zz', status: 'OutOfService' }, names))
      .toMatchObject({ homeStation: 'Unknown station', active: false });
  });

  it('maps hospital status to ER status and keeps the death tally', () => {
    const dto = {
      id: 'h', name: 'H', location: { latitude: 43.4, longitude: -80.3, address: null },
      erStatus: 'Diverting', availableBeds: 0, updatedAt: '2026-11-25T15:00:00Z', distanceKm: null,
    };
    expect(toHospital(dto, { id: 'h', name: 'H', erStatus: 'Open', deaths: 3 }))
      .toEqual({ id: 'h', name: 'H', erStatus: 'Diverting', deaths: 3 });
  });
});

describe('userFromToken', () => {
  it('reads the emergency role and name from the JWT (SH-S02)', () => {
    expect(userFromToken(jwt({ name: 'Leanne', role: ['emergency:Dispatcher', 'budget:User'] })))
      .toEqual({ name: 'Leanne', role: 'Dispatcher' });
  });

  it('uses the strongest emergency role', () => {
    expect(userFromToken(jwt({ sub: 'x', roles: ['emergency:Viewer', 'emergency:Admin'] })).role).toBe('Admin');
  });

  it("understands Registration's plain role names (Manager, Dispatcher, User)", () => {
    expect(userFromToken(jwt({ role: 'Manager' })).role).toBe('Admin');
    expect(userFromToken(jwt({ role: ['User', 'Dispatcher'] })).role).toBe('Dispatcher');
    expect(userFromToken(jwt({ role: 'User' })).role).toBe('Viewer');
    expect(userFromToken(jwt({ role: 'Police Officer' })).role).toBe('Viewer');
  });

  it('is a read-only Viewer without a token, a bad token or an emergency role', () => {
    expect(userFromToken(undefined).role).toBe('Viewer');
    expect(userFromToken('not-a-jwt').role).toBe('Viewer');
    expect(userFromToken(jwt({ role: 'transit:Admin' })).role).toBe('Viewer');
  });
});
