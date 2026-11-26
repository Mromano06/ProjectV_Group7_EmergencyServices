/**
 * @file TopBar.tsx
 * @brief Displays API connection state, signed-in user, and active weather warnings.
 */

import { useAuth } from '../auth/AuthContext';
import { useDispatchStore, type Connection } from '../state/DispatchContext';
import { formatUntil } from '../utils/Labels';
import '../styles/Panels.css';
import '../styles/Login.css';

// Always-visible bar above the panels: connection status, who is signed in (with Sign out), and every
// active weather / public warning (ES-FR-022 "app banner", issue #21). Warnings come from
// GET /weather-warnings?active=true and refresh every 30 seconds.

const CONNECTION_LABEL: Record<Connection, string> = {
  connecting: 'Connecting to the API…',
  online: 'Live – updates every 5 seconds',
  offline: 'Offline – retrying',
};

export default function TopBar() {
  const { user, usingDevToken, signOut } = useAuth();
  const { warnings, warningsState, warningsError, connection, now } = useDispatchStore();

  // Most serious first, then the one ending soonest.
  const RANK: Record<string, number> = { Warning: 0, Watch: 1, Advisory: 2, Statement: 3 };
  const sorted = [...warnings].sort((a, b) =>
    (RANK[a.severity] ?? 9) - (RANK[b.severity] ?? 9) || a.endsAt.localeCompare(b.endsAt));

  return (
    <header className="topbar">
      <div className="topbar-status">
        <strong className="topbar-title">Emergency Services</strong>
        <span className={`conn-dot conn-${connection}`} aria-hidden="true" />
        <span role="status">{CONNECTION_LABEL[connection]}</span>
        <span className="topbar-user">· {user.name} ({user.role})</span>
        {!usingDevToken && (
          <button type="button" className="signout-btn" onClick={() => signOut('You signed out.')}>
            Sign out
          </button>
        )}
      </div>

      <section className="topbar-warnings" aria-label="Active weather warnings">
        {warningsState === 'loading' ? (
          <span className="wx-empty">Loading weather warnings…</span>
        ) : warningsState === 'error' ? (
          <span className="wx-empty wx-error" role="alert">Could not load warnings: {warningsError}</span>
        ) : sorted.length === 0 ? (
          <span className="wx-empty">No active weather warnings</span>
        ) : (
          <ul className="wx-list">
            {sorted.map((w) => (
              <li
                key={w.id}
                className={`wx-chip wx-${w.severity}`}
                title={w.description ?? `${w.severity}: ${w.type} (${w.area})`}
              >
                <span className="wx-sev">{w.severity}</span>
                <strong>{w.type}</strong>
                <span>· {w.area}</span>
                <span className="wx-until">· until {formatUntil(w.endsAt, now)}</span>
                {w.affectsTransit && <span className="wx-transit">· 🚌 affects transit</span>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </header>
  );
}
