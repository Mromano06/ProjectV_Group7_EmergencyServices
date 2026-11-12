import { useAuth } from '../auth/AuthContext';
import { canManageDispatch } from '../auth/Permissions';
import { useDispatchStore } from '../state/DispatchContext';
import type { ActiveDispatch, DispatchPhase } from '../types/models';
import { getProgress } from '../utils/DispatchMath';
import UnitBadge from '../components/UnitBadge';
import '../styles/panels.css';

const PHASE_LABEL: Record<DispatchPhase, string> = {
  outbound: 'En route',
  onScene: 'On scene',
  returning: 'Returning',
};

function formatRemaining(ms: number) {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const m = String(Math.floor(total / 60)).padStart(2, '0');
  const s = String(total % 60).padStart(2, '0');
  return `${m}:${s}`;
}

interface RowProps {
  d: ActiveDispatch;
  now: number;
  canCancel: boolean;
  onCancel: (id: number) => void;
}

function DispatchRow({ d, now, canCancel, onCancel }: RowProps) {
  const progress = getProgress(d, now);
  const remaining = d.phaseStartedAt + d.phaseDurationMs - now;

  return (
    <li className="dispatch-row">
      <div className="dispatch-main">
        <div className="dispatch-places">
          <span className="place">{d.startLabel}</span>
          <span className="place place-end">{d.endLabel}</span>
        </div>
        <div className="dispatch-track">
          <span className="track-line" />
          <span className="track-dot track-dot-start" />
          <span className="track-dot track-dot-end" />
          <div
            className={`track-unit${d.phase === 'returning' ? ' returning' : ''}`}
            style={{ left: `${progress * 100}%` }}
          >
            <UnitBadge type={d.unitType} />
          </div>
        </div>
      </div>

      <div className="dispatch-side">
        <span className={`phase-label phase-${d.phase}`}>{PHASE_LABEL[d.phase]}</span>
        <span className="dispatch-timer">{formatRemaining(remaining)}</span>
        <span className="dispatch-callsign">{d.callSign}</span>
        {canCancel && d.phase !== 'returning' && (
          <button className="panel-btn danger" onClick={() => onCancel(d.id)}>
            Cancel
          </button>
        )}
      </div>
    </li>
  );
}

export default function DispatchTracker() {
  const { user } = useAuth();
  const { dispatches, now, cancelDispatch } = useDispatchStore();
  const canCancel = canManageDispatch(user.role);

  return (
    <section className="panel panel-tracker" aria-label="Units en route">
      <header className="panel-header">
        <h2>Units out ({dispatches.length})</h2>
      </header>
      {dispatches.length === 0 ? (
        <p className="panel-empty">No units out.</p>
      ) : (
        <ul className="panel-list">
          {dispatches.map((d) => (
            <DispatchRow key={d.id} d={d} now={now} canCancel={canCancel} onCancel={cancelDispatch} />
          ))}
        </ul>
      )}
    </section>
  );
}