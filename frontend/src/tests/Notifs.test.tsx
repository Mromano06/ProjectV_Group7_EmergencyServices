import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { ApiError } from '../api/apiClient';
import { AuthProvider } from '../auth/AuthContext';
import { DispatchProvider } from '../state/DispatchContext';
import type { Role } from '../types/models';

// The console talks to the API only through emergencyApi, so the tests replace those calls
// with fakes (vi.fn) and keep the real converters. No server needed.
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
}));

vi.mock('../api/emergencyApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/emergencyApi')>()),
  emergencyApi: api,
}));

// Units arrive almost immediately, so a full call can finish inside one test.
const travel = vi.hoisted(() => ({ ms: 40_000 }));
vi.mock('../utils/DispatchMath', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../utils/DispatchMath')>()),
  estimateTravelMs: () => travel.ms,
}));

const STATION = { id: 'st-1', name: 'Station 1', type: 'Fire', latitude: 43.36, longitude: -80.31, address: null, units: [] };
const ENGINE = { id: 'u-engine', callSign: 'Engine 1', type: 'Engine', stationId: 'st-1', status: 'Available' };
const MEDIC = { id: 'u-medic', callSign: 'Medic 2', type: 'Ambulance', stationId: 'st-1', status: 'Available' };

function incident(id: string, description: string, type = 'Fire', severity = 1, status = 'Reported') {
  return {
    id, type, severity, status, latitude: 43.36, longitude: -80.31,
    reportedAt: new Date().toISOString(), address: `${id} King St`, description,
  };
}

const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 100, totalCount: items.length });

let incidents: ReturnType<typeof incident>[];

beforeEach(() => {
  travel.ms = 40_000;
  incidents = [incident('inc-1', 'Structure fire'), incident('inc-2', 'Cardiac arrest', 'Medical', 2)];
  api.listUnits.mockImplementation(async () => paged([ENGINE, MEDIC]));
  api.listStations.mockResolvedValue([STATION]);
  api.listHospitals.mockResolvedValue([
    { id: 'h-1', name: 'General Hospital', location: { latitude: 43.38, longitude: -80.31, address: 'x' },
      erStatus: 'Open', availableBeds: 9, updatedAt: new Date().toISOString(), distanceKm: null },
  ]);
  api.listOpenIncidents.mockImplementation(async () => paged(incidents));
  api.createIncident.mockImplementation(async () => {
    incidents = [...incidents, incident('inc-3', 'Cat stuck in tree', 'Rescue', 4)];
    return { id: 'inc-3' };
  });
  api.assignUnits.mockResolvedValue([{ id: 'asg-1', incidentId: 'inc-1', unitId: 'u-engine', assignedAt: '', arrivedAt: null, releasedAt: null }]);
  api.changeStatus.mockResolvedValue({});
  api.fileReport.mockResolvedValue({});
  api.releaseAssignment.mockResolvedValue({});
  api.activeWarnings.mockResolvedValue([]);
  api.simulateWarning.mockResolvedValue({});
  api.getBudget.mockResolvedValue({
    departmentCode: 'EMS', departmentName: 'Emergency Services',
    allocated: 4500000, spent: 0, remaining: 4500000, currency: 'CAD',
  });
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

const callsLoaded = (n: number) => screen.findByText(new RegExp(`Notifications \\(${n}\\)`));

async function sendEngineToFirstCall() {
  const user = userEvent.setup();
  await callsLoaded(2);
  await user.click(screen.getAllByRole('button', { name: /set as destination/i })[0]);
  await user.click(screen.getByRole('button', { name: /Engine 1/ }));
  await user.click(screen.getByRole('button', { name: 'Send Engine 1' }));
}

describe('Dispatch console (talking to the API)', () => {
  it('shows the open incidents from the API', async () => {
    showApp();
    expect(await callsLoaded(2)).toBeInTheDocument();
    const panel = screen.getByRole('region', { name: 'Notifications' });
    expect(within(panel).getByText('Structure fire')).toBeInTheDocument();
    expect(within(panel).getByText(/inc-1 King St/)).toBeInTheDocument();
    // New calls are also logged
    expect(screen.getByText('New call: Structure fire at inc-1 King St')).toBeInTheDocument();
  });

  it('shows "No unit sent" on every new call', async () => {
    showApp();
    await callsLoaded(2);
    expect(screen.getAllByText('No unit sent')).toHaveLength(2);
  });

  it('lists available units with their station name', async () => {
    showApp();
    expect(await screen.findByText('Available units (2)')).toBeInTheDocument();
    expect(screen.getAllByText(/Station 1/).length).toBeGreaterThan(0);
  });

  it('+ Test call creates a real incident through the API', async () => {
    const user = userEvent.setup();
    showApp();
    await callsLoaded(2);

    await user.click(screen.getByRole('button', { name: '+ Test call' }));

    expect(api.createIncident).toHaveBeenCalledTimes(1);
    const body = api.createIncident.mock.calls[0][0];
    expect(body.location.latitude).toBeTypeOf('number');
    expect(await callsLoaded(3)).toBeInTheDocument();
  });

  it('lets a Dispatcher set a destination', async () => {
    const user = userEvent.setup();
    showApp();
    await callsLoaded(2);

    await user.click(screen.getAllByRole('button', { name: /set as destination/i })[0]);

    expect(screen.getByRole('button', { name: /destination set/i })).toBeInTheDocument();
  });

  it('hides dispatch controls from a Viewer', async () => {
    showApp('Viewer');
    await callsLoaded(2);

    expect(screen.queryByRole('button', { name: /set as destination/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ Test call' })).not.toBeInTheDocument();
  });

  it('sending a unit assigns it in the API and moves the call to En Route', async () => {
    showApp();
    await sendEngineToFirstCall();

    await waitFor(() => expect(api.assignUnits).toHaveBeenCalledWith('inc-1', ['u-engine']));
    expect(api.changeStatus).toHaveBeenCalledWith('inc-1', 'EnRoute');
    expect(await screen.findByText('Units out (1)')).toBeInTheDocument();
  });

  it('shows the API error and sends nothing when the assignment is refused', async () => {
    api.assignUnits.mockRejectedValue(new ApiError(409, 'unit_unavailable', 'Unit is unavailable.'));
    showApp();
    await sendEngineToFirstCall();

    expect(await screen.findByText(/Could not send Engine 1/)).toBeInTheDocument();
    expect(screen.getByText('Unit is unavailable.')).toBeInTheDocument();
    expect(screen.getByText('Units out (0)')).toBeInTheDocument();
  });

  it('when the right unit arrives, the incident is closed with an after-action report', async () => {
    travel.ms = 0; // arrives on the next clock tick
    showApp('Admin');
    await sendEngineToFirstCall();

    await waitFor(() => expect(api.fileReport).toHaveBeenCalled(), { timeout: 4000 });
    expect(api.changeStatus).toHaveBeenCalledWith('inc-1', 'OnScene');
    expect(api.changeStatus).toHaveBeenCalledWith('inc-1', 'Resolved');
    const [id, report] = api.fileReport.mock.calls[0];
    expect(id).toBe('inc-1');
    expect(report.outcome).toMatch(/Successful|Unsuccessful/);
    expect(report.deaths).toBeGreaterThanOrEqual(0);
  });

  it('recalling a unit releases its assignment in the API', async () => {
    const user = userEvent.setup();
    showApp();
    await sendEngineToFirstCall();
    const tracker = await screen.findByRole('region', { name: 'Units en route' });

    await user.click(within(tracker).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(api.releaseAssignment).toHaveBeenCalledWith('inc-1', 'asg-1'));
  });

  it('shows Offline and logs it once when the API cannot be reached', async () => {
    api.listOpenIncidents.mockRejectedValue(new ApiError(0, 'network_error', 'Cannot reach the Emergency API.'));
    showApp();

    expect(await screen.findByText(/Offline – retrying/)).toBeInTheDocument();
    expect(screen.getAllByText('Lost connection to the Emergency API')).toHaveLength(1);
  });

  it('shows active weather warnings from the API', async () => {
    api.activeWarnings.mockResolvedValue([{
      id: 'w-1', type: 'Freezing Rain', severity: 'Warning', area: 'Downtown', description: null,
      startsAt: new Date().toISOString(), endsAt: new Date(Date.now() + 3600_000).toISOString(),
      status: 'Active', source: 'Simulated', affectsTransit: true,
    }]);
    showApp();

    expect(await screen.findByRole('heading', { name: 'Freezing Rain' })).toBeInTheDocument();
    expect(screen.getByText('This warning affects transit.')).toBeInTheDocument();
  });
});
