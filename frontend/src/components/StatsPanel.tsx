import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { ApiError } from '../api/apiClient';
import { emergencyApi, OPEN_STATUSES, toStats, type IncidentStats } from '../api/emergencyApi';
import type { IncidentStatus, IncidentType } from '../types/models';
import '../styles/Panels.css';

// ES-FR-024: incident statistics for a date range (or all time), from GET /stats.
// Opens as a pop-up window (a modal <dialog>) from the event log's "Stats" button: there is no
// room for a fixed panel. It only exists while open, so it only fetches while someone looks at it.

const REFRESH_MS = 60_000;
const ALL_TIME_FROM = '2000-01-01T00:00:00.000Z'; // "From" left empty = since the first incident

// Types shown in "By type", in this order, with 0 when missing. Only these are shown, even if the API sends others.
const TYPES: IncidentType[] = ['Fire', 'Medical', 'Rescue'];
// Statuses are shown in this order; any other status the API sends is added after.
const STATUSES: IncidentStatus[] = [...OPEN_STATUSES, 'Resolved', 'Closed'];
const STATUS_LABEL: Record<string, string> = { EnRoute: 'En route', OnScene: 'On scene' };

const messageOf = (e: unknown) => (e instanceof ApiError || e instanceof Error ? e.message : String(e));

/** A local calendar day as yyyy-mm-dd (what <input type="date"> uses). */
function localDay(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function daysAgo(n: number) {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return localDay(d);
}

/** Local days → UTC instants: start of `from` (or all time) up to the start of the day after `to`. */
function toUtcRange(from: string, to: string) {
  const [ty, tm, td] = to.split('-').map(Number);
  let fromUtc = ALL_TIME_FROM;
  if (from) {
    const [fy, fm, fd] = from.split('-').map(Number);
    fromUtc = new Date(fy, fm - 1, fd).toISOString();
  }
  return { from: fromUtc, to: new Date(ty, tm - 1, td + 1).toISOString() };
}

function duration(minutes: number | null) {
  if (minutes == null || minutes < 0) return '–';
  const seconds = Math.round(minutes * 60);
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  if (h > 0) return `${h} h ${m} min`;
  return m > 0 ? `${m} min ${s} s` : `${s} s`;
}

const CLOCK = new Intl.DateTimeFormat('en-CA', { hour: 'numeric', minute: '2-digit' });

function countRows(known: readonly string[], counts: Record<string, number>) {
  const extra = Object.keys(counts).filter((k) => !known.includes(k));
  return [...known, ...extra].map((key) => ({ key, count: counts[key] ?? 0 }));
}

const PRESETS: { label: string; days: number | null }[] = [
  { label: 'Today', days: 0 },
  { label: '7 days', days: 6 },
  { label: '30 days', days: 29 },
  { label: 'All time', days: null },
];

export default function StatsPanel({ onClose }: { onClose: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();

  // Open as a modal: it sits above the fixed panels, keeps focus inside and closes on Escape.
  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const [from, setFrom] = useState('');                       // '' = all time
  const [to, setTo] = useState(() => localDay(new Date()));

  // Each result remembers which range it is for, so data from an old range is never shown as the new one.
  const [result, setResult] = useState<{ key: string; stats: IncidentStats; at: number } | null>(null);
  const [error, setError] = useState<{ key: string; text: string } | null>(null);
  const latestRequest = useRef(0);

  const today = localDay(new Date());
  const rangeKey = `${from}|${to}`;
  const rangeError = !to ? 'Pick an end date.' : from && from > to ? 'The start date is after the end date.' : null;

  // State is only set when the request finishes (never synchronously inside the effect below).
  const load = useCallback(() => {
    if (rangeError) return Promise.resolve();
    const id = ++latestRequest.current;
    const range = toUtcRange(from, to);
    return emergencyApi.getStats(range.from, range.to).then(
      (dto) => {
        if (id !== latestRequest.current) return; // a newer range was asked for meanwhile
        setResult({ key: rangeKey, stats: toStats(dto), at: Date.now() });
        setError(null);
      },
      (e: unknown) => {
        if (id !== latestRequest.current) return;
        setError({ key: rangeKey, text: messageOf(e) });
      },
    );
  }, [from, to, rangeKey, rangeError]);

  useEffect(() => {
    void load();
    const timer = setInterval(() => void load(), REFRESH_MS);
    return () => clearInterval(timer);
  }, [load]);

  const shown = result?.key === rangeKey ? result : null;
  const stats = shown?.stats ?? null;
  const statsFailed = error?.key === rangeKey ? error.text : null;

  return (
    <dialog
      ref={dialogRef}
      className="stats-dialog"
      aria-labelledby={titleId}
      onClose={onClose} // Escape
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }} // click on the dimmed backdrop
    >
      <header className="panel-header">
        <h2 id={titleId}>Statistics</h2>
        <div className="panel-header-actions">
          <button className="panel-btn" onClick={() => void load()} disabled={rangeError !== null}>Refresh</button>
          <button className="panel-btn" onClick={onClose} aria-label="Close statistics">✕</button>
        </div>
      </header>

      <div className="stats-body">
        <div className="stats-range">
          <label htmlFor="stats-from">From</label>
          <input id="stats-from" type="date" value={from} max={to || today}
            onChange={(e) => setFrom(e.target.value)} aria-describedby="stats-from-hint" />
          <label htmlFor="stats-to">To</label>
          <input id="stats-to" type="date" value={to} min={from || undefined} max={today}
            onChange={(e) => setTo(e.target.value)} />
          <div className="stats-presets">
            {PRESETS.map((p) => {
              const active = p.days === null ? from === '' && to === today : from === daysAgo(p.days) && to === today;
              return (
                <button key={p.label} type="button" className={`panel-btn${active ? ' active' : ''}`}
                  aria-pressed={active}
                  onClick={() => { setFrom(p.days === null ? '' : daysAgo(p.days)); setTo(today); }}>
                  {p.label}
                </button>
              );
            })}
          </div>
        </div>
        <p id="stats-from-hint" className="budget-hint">
          {from ? '' : 'All time · '}
          {shown ? `Updated ${CLOCK.format(shown.at)}` : 'Leave "From" empty for all time.'}
        </p>

        {rangeError ? (
          <p className="form-msg form-error" role="alert">{rangeError}</p>
        ) : stats ? (
          <>
            <div className="stats-cards">
              <div className="stat">
                <span className="stat-num">{stats.total}</span>
                <span className="stat-label">Incidents</span>
              </div>
              <div className="stat stat-good">
                <span className="stat-num">{stats.completed}</span>
                <span className="stat-label">Completed</span>
              </div>
              <div className="stat">
                <span className="stat-num">{duration(stats.averageResponseMinutes)}</span>
                <span className="stat-label">Avg response</span>
              </div>
              <div className="stat stat-death">
                <span className="stat-num">{stats.deaths}</span>
                <span className="stat-label">Deaths</span>
              </div>
            </div>
            <p className="budget-hint">
              Deaths come from filed after-action reports. The event log's count only covers this browser session.
              {stats.averageResponseMinutes == null && stats.total > 0 && ' No unit has arrived on scene in this range yet.'}
            </p>

            <div className="stats-tables">
              <table className="stats-table">
                <caption>By type</caption>
                <tbody>
                  {TYPES.map((type) => (
                    <tr key={type}><th scope="row">{type}</th><td>{stats.byType[type] ?? 0}</td></tr>
                  ))}
                </tbody>
              </table>
              <table className="stats-table">
                <caption>By status</caption>
                <tbody>
                  {countRows(STATUSES, stats.byStatus).map((r) => (
                    <tr key={r.key}><th scope="row">{STATUS_LABEL[r.key] ?? r.key}</th><td>{r.count}</td></tr>
                  ))}
                </tbody>
              </table>
            </div>

            {statsFailed && <p className="budget-hint">Showing the last known values – {statsFailed}</p>}
          </>
        ) : statsFailed ? (
          <p className="form-msg form-error" role="alert">Statistics unavailable: {statsFailed}</p>
        ) : (
          <p className="panel-empty">Loading statistics…</p>
        )}
      </div>
    </dialog>
  );
}