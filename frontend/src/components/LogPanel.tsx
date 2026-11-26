/**
 * @file LogPanel.tsx
 * @brief Displays dispatch activity and completion counts for the current session.
 *
 * The Statistics panel displays incident statistics retrieved from the API.
 */

import { useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { canManageDispatch, isAdmin } from '../auth/Permissions';
import { useDispatchStore } from '../state/DispatchContext';
import type { LogEntry, LogLevel } from '../types/models';
import { formatClock } from '../utils/Labels';
import StatsPanel from './StatsPanel';
import '../styles/Panels.css';

const LEVEL_ICON: Record<LogLevel, string> = { info: '•', success: '✔', warning: '▲', danger: '✖' };

function handleRespond(entry: LogEntry) {
  console.log('Respond to log entry', entry);
}
/**
 * Shows dispatch activity and completion counts for this browser session.
 * The Stats panel displays statistics retrieved from the API.
 */
export default function LogPanel() {
  const { user } = useAuth();
  const { logs, records, hospitals, addTestWarning, clearLog } = useDispatchStore();
  const canManage = canManageDispatch(user.role);
  const [statsOpen, setStatsOpen] = useState(false); // ES-FR-024 pop-up

  const successful = records.filter((r) => r.successful).length;
  const totalDeaths = hospitals.reduce((sum, h) => sum + h.deaths, 0);

  return (
    <section className="panel panel-log" aria-label="Event log">
      <header className="panel-header">
        <h2>Event log ({logs.length})</h2>
        <div className="panel-header-actions">
          <button className="panel-btn" onClick={() => setStatsOpen(true)} aria-haspopup="dialog">Stats</button>
          {isAdmin(user.role) && (
            <button className="panel-btn" onClick={() => void addTestWarning()}>+ Test weather</button>
          )}
          {canManage && <button className="panel-btn" onClick={clearLog}>Clear</button>}
        </div>
      </header>

      <div className="log-stats">
        <div className="stat">
          <span className="stat-num">{records.length}</span>
          <span className="stat-label">Completed</span>
        </div>
        <div className="stat stat-good">
          <span className="stat-num">{successful}</span>
          <span className="stat-label">Successful</span>
        </div>
        <div className="stat stat-bad">
          <span className="stat-num">{records.length - successful}</span>
          <span className="stat-label">Unsuccessful</span>
        </div>
        <div className="stat stat-death">
          <span className="stat-num">{totalDeaths}</span>
          <span className="stat-label">Deaths</span>
        </div>
      </div>

      <ul className="hospital-row" aria-label="Deaths by hospital">
        {hospitals.map((h) => (
          <li key={h.id} className={`hospital-chip er-${h.erStatus}`} title={`ER ${h.erStatus}`}>
            <span className="er-dot" />
            {h.name}
            <strong>{h.deaths}</strong>
          </li>
        ))}
      </ul>

      {logs.length === 0 ? (
        <p className="panel-empty">Nothing logged yet.</p>) : (
        <ul className="panel-list log-list">
          {logs.map((e) => (
            <li key={e.id} className={`log-entry lvl-${e.level}`}>
              <div className="log-head">
                <span className="log-icon" aria-hidden="true">{LEVEL_ICON[e.level]}</span>
                <span className="log-cat">{e.category}</span>
                <time className="log-time">{formatClock(e.time)}</time>
              </div>

              <p className="log-msg">{e.message}</p>

              {e.details && e.details.length > 0 && (
                <ul className="log-details">
                  {e.details.map((line, i) => <li key={i}>{line}</li>)}
                </ul>
              )}

              {canManage && (
                <div className="log-actions">
                  <button className="log-btn" onClick={() => handleRespond(e)}>Respond</button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {statsOpen && <StatsPanel onClose={() => setStatsOpen(false)} />}
    </section>
  );
}