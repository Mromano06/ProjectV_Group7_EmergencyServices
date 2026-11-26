/**
 * @file RoadClosure.tsx
 * @brief Road-closure controls on an incident card: report a blocked road to Public Transport,
 *        reopen it, and see closures already reported nearby (ES-FR-015, ES-FR-010).
 *
 * Everything stays hidden behind one small "Road closure" button so the card doesn't get busier.
 * Clicking it opens a short form plus a list of nearby closures, inside the same card.
 */

import { useEffect, useState } from 'react';
import { ApiError } from '../api/apiClient';
import { emergencyApi, type NearbyRoadClosureDto } from '../api/emergencyApi';
import { REOPEN_HOURS, RESTRICTION_TYPES, type ActiveClosure } from '../state/RoadClosures';
import type { EmergencyNotice, LogEntry } from '../types/models';
import { formatUntil } from '../utils/Labels';

const messageOf = (e: unknown) =>
  e instanceof ApiError && e.code === 'transit_unavailable'
    ? "Public Transport didn't accept it. Try again in a moment."
    : e instanceof Error ? e.message : String(e);

type Log = (entry: Omit<LogEntry, 'id' | 'time'>) => void;

interface ControlsProps {
  notice: EmergencyNotice;
  closure?: ActiveClosure;
  now: number;
  onSent: (closure: ActiveClosure) => void;
  onCleared: () => void;
  log: Log;
}

/** The button (and, when open, the form) for one incident card. */
export function RoadClosureControls({ notice, closure, now, onSent, onCleared, log }: ControlsProps) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reopen = async () => {
    setBusy(true);
    setError(null);
    try {
      await emergencyApi.clearRoadRestriction(notice.id);
      onCleared();
      log({ level: 'success', category: 'call', message: `Road reopened: ${notice.locationLabel}`,
        details: ['Public Transport notified'] });
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setBusy(false);
    }
  };

  if (closure) {
    return (
      <>
        <button className="notice-btn" onClick={() => void reopen()} disabled={busy}>
          {busy ? 'Reopening…' : 'Reopen road'}
        </button>
        {error && <p className="road-error" role="alert">{error}</p>}
      </>
    );
  }

  return (
    <>
      <button
        className={`notice-btn${open ? ' active' : ''}`}
        aria-expanded={open}
        onClick={() => { setOpen(!open); setError(null); }}
      >
        🚧 Road closure
      </button>
      {open && (
        <RoadClosureForm
          notice={notice}
          now={now}
          onCancel={() => setOpen(false)}
          onSent={(c) => {
            setOpen(false);
            onSent(c);
            log({
              level: 'warning', category: 'call',
              message: `Road closure sent to Transit: ${notice.locationLabel}`,
              details: [`${c.restrictionType} · until ${formatUntil(new Date(c.until).toISOString(), now)}`],
            });
          }}
        />
      )}
    </>
  );
}

interface FormProps {
  notice: EmergencyNotice;
  now: number;
  onCancel: () => void;
  onSent: (closure: ActiveClosure) => void;
}

function RoadClosureForm({ notice, now, onCancel, onSent }: FormProps) {
  const [area, setArea] = useState(notice.locationLabel);
  const [type, setType] = useState<string>(RESTRICTION_TYPES[0]);
  const [hours, setHours] = useState<number>(2);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const send = async (e: React.FormEvent) => {
    e.preventDefault();
    if (area.trim() === '') {
      setError('Say which road or area is affected.');
      return;
    }
    setBusy(true);
    setError(null);
    const until = Date.now() + hours * 3_600_000;
    try {
      await emergencyApi.reportRoadRestriction(notice.id, {
        affectedArea: area.trim(),
        restrictionType: type,
        estimatedEndAt: new Date(until).toISOString(),
      });
      onSent({ until, restrictionType: type });
    } catch (err) {
      setError(messageOf(err));
      setBusy(false);
    }
  };

  return (
    <form className="road-form" onSubmit={(e) => void send(e)} aria-label="Report road closure">
      <label className="road-field">
        <span>Affected road</span>
        <input value={area} maxLength={300} onChange={(e) => setArea(e.target.value)} />
      </label>
      <div className="road-row">
        <label className="road-field">
          <span>Type</span>
          <select value={type} onChange={(e) => setType(e.target.value)}>
            {RESTRICTION_TYPES.map((t) => <option key={t}>{t}</option>)}
          </select>
        </label>
        <label className="road-field">
          <span>Reopens in</span>
          <select value={hours} onChange={(e) => setHours(Number(e.target.value))}>
            {REOPEN_HOURS.map((h) => <option key={h} value={h}>{h === 1 ? '1 hour' : `${h} hours`}</option>)}
          </select>
        </label>
      </div>
      <p className="road-hint">
        Reopens around {formatUntil(new Date(now + hours * 3_600_000).toISOString(), now)}. Public Transport
        will re-route buses.
      </p>
      {error && <p className="road-error" role="alert">{error}</p>}
      <div className="road-row">
        <button type="submit" className="notice-btn active" disabled={busy}>
          {busy ? 'Sending…' : 'Send to Transit'}
        </button>
        <button type="button" className="notice-btn" onClick={onCancel} disabled={busy}>Cancel</button>
      </div>
      <NearbyClosures incidentId={notice.id} now={now} />
    </form>
  );
}

/** Closures other services already reported near this incident (ES-FR-010), loaded when the form opens. */
function NearbyClosures({ incidentId, now }: { incidentId: string; now: number }) {
  const [closures, setClosures] = useState<NearbyRoadClosureDto[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    emergencyApi.nearbyRoadClosures(incidentId)
      .then((list) => { if (!cancelled) setClosures(list ?? []); })
      .catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [incidentId]);

  return (
    <div className="road-nearby" aria-label="Closures nearby">
      <span className="road-nearby-title">Already closed nearby</span>
      {failed ? (
        <p className="road-muted">Couldn't load closures from Public Transport.</p>
      ) : closures === null ? (
        <p className="road-muted">Loading…</p>
      ) : closures.length === 0 ? (
        <p className="road-muted">None reported.</p>
      ) : (
        <ul>
          {closures.slice(0, 3).map((c) => (
            <li key={c.id}>
              {c.description?.trim() || 'Road closure'}
              <span className="road-muted">
                {c.distanceKm != null ? ` · ${c.distanceKm.toFixed(1)} km` : ' · same street'}
                {c.expectedEndAt ? ` · until ${formatUntil(c.expectedEndAt, now)}` : ''}
              </span>
            </li>
          ))}
          {closures.length > 3 && <li className="road-muted">+{closures.length - 3} more</li>}
        </ul>
      )}
    </div>
  );
}