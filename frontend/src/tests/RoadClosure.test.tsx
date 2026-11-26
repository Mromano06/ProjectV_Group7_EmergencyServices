/**
 * @file RoadClosure.test.tsx
 * @brief Tests reporting a closed road to Public Transport from an incident card, reopening it,
 *        and showing closures nearby (ES-FR-015, ES-FR-010).
 */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { ApiError } from '../api/apiClient';
import { AuthProvider } from '../auth/AuthContext';
import { DispatchProvider } from '../state/DispatchContext';
import type { Role } from '../types/models';

// Same fakes as Notifs.test.tsx, plus the three road-closure calls.
const api = vi.hoisted(() => ({
  listUnits: vi.fn(),
  listStations: vi.fn(),
  listHospitals: vi.fn(),
  listOpenIncidents: vi.fn(),
  createIncident: vi.fn(),
  assignUnits: vi.fn(),
  changeStatus: vi.fn(),
  fileReport: vi.fn(),
  releaseAssignment: vi.fn(),
  activeWarnings: vi.fn(),
  simulateWarning: vi.fn(),
  getBudget: vi.fn(),
  submitExpense: vi.fn(),
  requestFunding: vi.fn(),
  reportRoadRestriction: vi.fn(),
  clearRoadRestriction: vi.fn(),
  nearbyRoadClosures: vi.fn(),
}));

vi.mock('../api/emergencyApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/emergencyApi')>()),
  emergencyApi: api,
}));

const STATION = { id: 'st-1', name: 'Station 1', type: 'Fire', latitude: 43.36, longitude: -80.31, address: null, units: [] };
const ENGINE = { id: 'u-engine', callSign: 'Engine 1', type: 'Engine', stationId: 'st-1', status: 'Available' };
const INCIDENT = {
  id: 'inc-1', type: 'Fire', severity: 2, status: 'Reported', latitude: 43.36, longitude: -80.31,
  reportedAt: new Date().toISOString(), address: '100 King St W, Cambridge',
  description: 'Two-vehicle collision blocking both lanes',
};
const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 100, totalCount: items.length });

beforeEach(() => {
  sessionStorage.clear();
  api.listUnits.mockResolvedValue(paged([ENGINE]));
  api.listStations.mockResolvedValue([STATION]);
  api.listHospitals.mockResolvedValue([]);
  api.listOpenIncidents.mockResolvedValue(paged([INCIDENT]));
  api.activeWarnings.mockResolvedValue([]);
  api.getBudget.mockResolvedValue({
    departmentCode: 'EMS', departmentName: 'Emergency Services',
    allocated: 4500000, spent: 0, remaining: 4500000, currency: 'CAD',
  });
  api.reportRoadRestriction.mockResolvedValue({ incidentId: 'inc-1', sentToTransit: true, sentAt: '', status: 'active' });
  api.clearRoadRestriction.mockResolvedValue({ incidentId: 'inc-1', sentToTransit: true, sentAt: '', status: 'resolved' });
  api.nearbyRoadClosures.mockResolvedValue([
    { id: '7', description: 'Water main repair on King St W', expectedEndAt: null,
      latitude: 43.361, longitude: -80.311, distanceKm: 0.4, matchedBy: 'distance' },
  ]);
});

function showApp(role: Role = 'Dispatcher') {
  render(
    <AuthProvider initialRole={role}>
      <DispatchProvider>
        <App />
      </DispatchProvider>
    </AuthProvider>,
  );
}

const card = async () => {
  await screen.findByText(/Notifications \(1\)/);
  return screen.getByRole('region', { name: 'Notifications' });
};

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  const panel = await card();
  await user.click(within(panel).getByRole('button', { name: /road closure/i }));
  return within(panel).getByRole('form', { name: 'Report road closure' });
}

describe('Road closures on an incident card (ES-FR-015)', () => {
  it('stays out of the way: one button, no form until it is clicked', async () => {
    showApp();
    const panel = await card();
    expect(within(panel).getByRole('button', { name: /road closure/i })).toHaveAttribute('aria-expanded', 'false');
    expect(within(panel).queryByRole('form', { name: 'Report road closure' })).not.toBeInTheDocument();
    expect(api.nearbyRoadClosures).not.toHaveBeenCalled(); // nothing loads until it's opened
  });

  it('is not shown to Viewers (SH-S05)', async () => {
    showApp('Viewer');
    const panel = await card();
    expect(within(panel).queryByRole('button', { name: /road closure/i })).not.toBeInTheDocument();
  });

  it('fills in the incident address and sends the closure to Transit', async () => {
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);

    expect(within(form).getByLabelText('Affected road')).toHaveValue('100 King St W, Cambridge');
    await user.selectOptions(within(form).getByLabelText('Type'), 'Lane closure');
    await user.selectOptions(within(form).getByLabelText('Reopens in'), '4');
    const before = Date.now();
    await user.click(within(form).getByRole('button', { name: 'Send to Transit' }));

    await waitFor(() => expect(api.reportRoadRestriction).toHaveBeenCalledTimes(1));
    const [id, body] = api.reportRoadRestriction.mock.calls[0];
    expect(id).toBe('inc-1');
    expect(body.affectedArea).toBe('100 King St W, Cambridge');
    expect(body.restrictionType).toBe('Lane closure');
    const end = new Date(body.estimatedEndAt).getTime();
    expect(end).toBeGreaterThanOrEqual(before + 4 * 3_600_000 - 1000);
    expect(end).toBeLessThanOrEqual(Date.now() + 4 * 3_600_000 + 1000);
  });

  it('after sending: shows a "Road closed" tag, offers Reopen, and logs it', async () => {
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);
    await user.click(within(form).getByRole('button', { name: 'Send to Transit' }));

    const panel = await card();
    expect(await within(panel).findByText(/Road closed · until/)).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Reopen road' })).toBeInTheDocument();
    expect(within(panel).queryByRole('form', { name: 'Report road closure' })).not.toBeInTheDocument();
    expect(screen.getByText('Road closure sent to Transit: 100 King St W, Cambridge')).toBeInTheDocument();
  });

  it('when Transit refuses, says so and shows no "Road closed" tag', async () => {
    api.reportRoadRestriction.mockRejectedValue(
      new ApiError(502, 'transit_unavailable', 'Public Transport did not accept the road restriction.'));
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);
    await user.click(within(form).getByRole('button', { name: 'Send to Transit' }));

    expect(await within(form).findByRole('alert')).toHaveTextContent(/didn't accept it/);
    expect(screen.queryByText(/Road closed · until/)).not.toBeInTheDocument();
  });

  it('needs an affected road', async () => {
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);
    await user.clear(within(form).getByLabelText('Affected road'));
    await user.click(within(form).getByRole('button', { name: 'Send to Transit' }));

    expect(within(form).getByRole('alert')).toHaveTextContent(/which road/);
    expect(api.reportRoadRestriction).not.toHaveBeenCalled();
  });

  it('Reopen road tells Transit and removes the tag', async () => {
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);
    await user.click(within(form).getByRole('button', { name: 'Send to Transit' }));
    const panel = await card();
    await user.click(await within(panel).findByRole('button', { name: 'Reopen road' }));

    await waitFor(() => expect(api.clearRoadRestriction).toHaveBeenCalledWith('inc-1'));
    await waitFor(() => expect(within(panel).queryByText(/Road closed · until/)).not.toBeInTheDocument());
    expect(within(panel).getByRole('button', { name: /road closure/i })).toBeInTheDocument();
    expect(screen.getByText('Road reopened: 100 King St W, Cambridge')).toBeInTheDocument();
  });

  it('remembers the closure after a page refresh (same tab)', async () => {
    const user = userEvent.setup();
    const first = render(
      <AuthProvider initialRole="Dispatcher"><DispatchProvider><App /></DispatchProvider></AuthProvider>,
    );
    const form = await openForm(user);
    await user.click(within(form).getByRole('button', { name: 'Send to Transit' }));
    await screen.findByText(/Road closed · until/);
    first.unmount();

    showApp();
    const panel = await card();
    expect(await within(panel).findByText(/Road closed · until/)).toBeInTheDocument();
  });
});

describe('Closures already reported nearby (ES-FR-010)', () => {
  it('lists them in the form with their distance', async () => {
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);

    expect(await within(form).findByText('Water main repair on King St W')).toBeInTheDocument();
    expect(within(form).getByText(/0\.4 km/)).toBeInTheDocument();
    expect(api.nearbyRoadClosures).toHaveBeenCalledWith('inc-1');
  });

  it('says when there are none', async () => {
    api.nearbyRoadClosures.mockResolvedValue([]);
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);
    expect(await within(form).findByText('None reported.')).toBeInTheDocument();
  });

  it("says when Transit can't be reached, and the form still works", async () => {
    api.nearbyRoadClosures.mockRejectedValue(new ApiError(503, 'transit_unavailable', 'down'));
    const user = userEvent.setup();
    showApp();
    const form = await openForm(user);
    expect(await within(form).findByText(/Couldn't load closures/)).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Send to Transit' })).toBeEnabled();
  });
});