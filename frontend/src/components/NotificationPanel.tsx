import { useAuth } from '../auth/AuthContext';
import { canManageDispatch } from '../auth/Permissions';
import { useDispatchStore } from '../state/DispatchContext';
import type { Severity } from '../types/models';
import '../styles/panels.css';

const SEVERITY_LABEL: Record<Severity, string> = {
  1: 'Critical', 2: 'High', 3: 'Medium', 4: 'Low',
};

export default function NotificationsPanel() {
  const { user } = useAuth();
  const { notices, dispatches, selectedNoticeId, selectNotice, addTestNotice } =
    useDispatchStore();
  const canManage = canManageDispatch(user.role);

  // Severity first, then oldest first
  const sorted = [...notices].sort((a, b) => a.severity - b.severity || a.createdAt - b.createdAt);

  return (
    <section className="panel panel-notices" aria-label="Notifications">
      <header className="panel-header">
        <h2>Notifications ({notices.length})</h2>
        {import.meta.env.DEV && (
          <button className="panel-btn" onClick={addTestNotice}>+ Test call</button>
        )}
      </header>

      {sorted.length === 0 ? (
        <p className="panel-empty">No active calls.</p>
      ) : (
        <ul className="panel-list">
          {sorted.map((n) => {
            const selected = n.id === selectedNoticeId;
            const responding = dispatches.filter(
              (d) => d.noticeId === n.id && d.phase !== 'returning',
            ).length;
            return (
              <li key={n.id} className={`notice sev-${n.severity}${selected ? ' selected' : ''}`}>
                <div className="notice-top">
                  <span className="sev-pill">{SEVERITY_LABEL[n.severity]}</span>
                  <span className="notice-type">{n.incidentType}</span>
                </div>
                <h3>{n.title}</h3>
                <p className="notice-loc">{n.locationLabel}</p>
                <p className="notice-meta">
                  Needs: {n.requiredUnitType} · Responding: {responding}
                </p>
                {canManage && (
                  <button
                    className="panel-btn"
                    onClick={() => selectNotice(selected ? null : n.id)}
                  >
                    {selected ? 'Selected as destination' : 'Set as destination'}
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}