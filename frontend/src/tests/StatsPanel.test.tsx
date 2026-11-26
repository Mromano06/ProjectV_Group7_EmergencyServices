/**
 * @file StatsPanel.test.tsx
 * @brief Tests statistics date ranges, displayed results, modal behavior, and errors.
 */


import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/apiClient';
import StatsPanel from '../components/StatsPanel';

// ES-FR-024: the stats window shows, for a date range (or all time), the incident counts by type
// and status, the average response time and deaths, all from GET /stats. The API call is faked;
// the real toStats converter is used, so the server's field names (deathCount…) are checked too.
const api = vi.hoisted(() => ({
  getStats: vi.fn(),
}));

vi.mock('../api/emergencyApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/emergencyApi')>()),
  emergencyApi: api,
}));

/** What the server sends for GET /stats (copied from a real response). */
const STATS = {
  from: '2026-11-01T00:00:00Z',
  to: '2026-11-26T00:00:00Z',
  totalIncidents: 12,
  byType: { Fire: 5, Medical: 6, Rescue: 1 },
  byStatus: { Closed: 8, Resolved: 2, EnRoute: 2 },
  averageResponseMinutes: 6.4,
  deathCount: 1,
};

const ALL_TIME_FROM = '2000-01-01T00:00:00.000Z';
/** Local midnight of a day, as the UTC instant the panel sends. Month is 1–12. */
const localMidnight = (y: number, m: number, d: number) => new Date(y, m - 1, d).toISOString();

beforeAll(() => {
  // jsdom has <dialog> but not showModal(); opening it is enough for these tests.
  if (typeof HTMLDialogElement.prototype.showModal !== 'function') {
    HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
  }
});

beforeEach(() => {
  vi.clearAllMocks();
  // Only the clock is faked ("today" is Wed 25 Nov 2026, noon); timers stay real for user-event.
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 25, 12, 0));
  api.getStats.mockResolvedValue(STATS);
});

afterEach(() => {
  vi.useRealTimers();
});

function showStats() {
  const onClose = vi.fn();
  render(<StatsPanel onClose={onClose} />);
  return onClose;
}

/** One of the big numbers at the top, found by its label. */
const card = (label: string) => screen.getByText(label, { selector: '.stat-label' }).parentElement as HTMLElement;

describe('Stats window (ES-FR-024)', () => {
  it('opens as a pop-up with all-time totals from the server', async () => {
    showStats();

    expect(screen.getByRole('dialog', { name: 'Statistics' })).toBeInTheDocument();
    await screen.findByText('12');
    expect(api.getStats).toHaveBeenCalledWith(ALL_TIME_FROM, localMidnight(2026, 11, 26));
    expect(screen.getByRole('button', { name: 'All time' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText(/All time · Updated/)).toBeInTheDocument();

    expect(within(card('Incidents')).getByText('12')).toBeInTheDocument();
    expect(within(card('Completed')).getByText('10')).toBeInTheDocument(); // Resolved 2 + Closed 8
    expect(within(card('Avg response')).getByText('6 min 24 s')).toBeInTheDocument();
    expect(within(card('Deaths')).getByText('1')).toBeInTheDocument(); // from deathCount
  });

  it('shows counts by type (Fire, Medical, Rescue only) and by status', async () => {
    api.getStats.mockResolvedValue({ ...STATS, byType: { ...STATS.byType, Hazmat: 3 } });
    showStats();
    await screen.findByText('12');

    const byType = screen.getByRole('table', { name: 'By type' });
    expect(within(byType).getByRole('row', { name: 'Fire 5' })).toBeInTheDocument();
    expect(within(byType).getByRole('row', { name: 'Medical 6' })).toBeInTheDocument();
    expect(within(byType).getByRole('row', { name: 'Rescue 1' })).toBeInTheDocument();
    expect(within(byType).queryByText('Hazmat')).not.toBeInTheDocument();
    expect(within(byType).queryByText('Other')).not.toBeInTheDocument();

    const byStatus = screen.getByRole('table', { name: 'By status' });
    expect(within(byStatus).getByRole('row', { name: 'En route 2' })).toBeInTheDocument();
    expect(within(byStatus).getByRole('row', { name: 'Resolved 2' })).toBeInTheDocument();
    expect(within(byStatus).getByRole('row', { name: 'Closed 8' })).toBeInTheDocument();
    expect(within(byStatus).getByRole('row', { name: 'Reported 0' })).toBeInTheDocument();
  });

  it('the presets ask for today and the last 7 days', async () => {
    const user = userEvent.setup();
    showStats();
    await screen.findByText('12');

    await user.click(screen.getByRole('button', { name: '7 days' }));
    await waitFor(() =>
      expect(api.getStats).toHaveBeenLastCalledWith(localMidnight(2026, 11, 19), localMidnight(2026, 11, 26)));
    expect(screen.getByRole('button', { name: '7 days' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Today' }));
    await waitFor(() =>
      expect(api.getStats).toHaveBeenLastCalledWith(localMidnight(2026, 11, 25), localMidnight(2026, 11, 26)));
  });

  it('asks for a picked date range, including the whole end day', async () => {
    showStats();
    await screen.findByText('12');

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-11-01' } });
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-11-10' } });

    await waitFor(() =>
      expect(api.getStats).toHaveBeenLastCalledWith(localMidnight(2026, 11, 1), localMidnight(2026, 11, 11)));
  });

  it('refuses a range that ends before it starts, without calling the API', async () => {
    showStats();
    await screen.findByText('12');
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-11-20' } });
    await waitFor(() => expect(api.getStats).toHaveBeenCalledTimes(2));

    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-11-10' } });

    expect(await screen.findByText('The start date is after the end date.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeDisabled();
    expect(api.getStats).toHaveBeenCalledTimes(2);
  });

  it('shows a dash when no unit has arrived on scene yet', async () => {
    api.getStats.mockResolvedValue({ ...STATS, averageResponseMinutes: null });
    showStats();
    await screen.findByText('12');

    expect(within(card('Avg response')).getByText('–')).toBeInTheDocument();
    expect(screen.getByText(/No unit has arrived on scene in this range yet\./)).toBeInTheDocument();
  });

  it('says clearly when the stats cannot be loaded', async () => {
    api.getStats.mockRejectedValue(new ApiError(403, 'forbidden', 'Your role is not allowed to do that.'));
    showStats();

    expect(await screen.findByText('Statistics unavailable: Your role is not allowed to do that.')).toBeInTheDocument();
  });

  it('keeps the last numbers when a refresh fails', async () => {
    const user = userEvent.setup();
    showStats();
    await screen.findByText('12');

    api.getStats.mockRejectedValueOnce(new ApiError(0, 'network_error', 'Cannot reach the Emergency API.'));
    await user.click(screen.getByRole('button', { name: 'Refresh' }));

    expect(await screen.findByText(/Showing the last known values – Cannot reach the Emergency API\./)).toBeInTheDocument();
    expect(within(card('Incidents')).getByText('12')).toBeInTheDocument();
  });

  it('closes with the ✕ button, Escape, or a click outside, but not a click inside', async () => {
    const user = userEvent.setup();
    const onClose = showStats();
    await screen.findByText('12');
    const dialog = screen.getByRole('dialog', { name: 'Statistics' });

    await user.click(screen.getByRole('heading', { name: 'Statistics' }));
    expect(onClose).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Close statistics' }));
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent(dialog, new Event('close')); // what the browser fires on Escape (jsdom doesn't handle the key)
    expect(onClose).toHaveBeenCalledTimes(2);

    await user.click(dialog); // the dimmed backdrop counts as the dialog itself
    expect(onClose).toHaveBeenCalledTimes(3);
  });
});