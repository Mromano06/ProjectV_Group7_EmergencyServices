import { useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { canManageDispatch } from '../auth/Permissions';
import { useDispatchStore } from '../state/DispatchContext';
import type { IncidentType, Severity, UnitType } from '../types/models';
import { UNIT_IMAGES } from '../utils/Images';

import '../styles/Panels.css';


const SEVERITY_LABEL: Record<Severity, string> = {
  1: 'Critical', 2: 'High', 3: 'Medium', 4: 'Low',
};

const TYPE_ICON: Record<IncidentType, string> = {
  Fire: '/units/houseonfire.png',
  Medical: '/units/medical.png', 
  Rescue: '🆘',
  Other: '❗',
};

const UNIT_ICON: Record<UnitType, string> = {
  FireTruck: '🚒', Ladder: '🪜', Ambulance: '🚑',
};

function formatAge(ms: number) {
  const total = Math.max(0, Math.floor(ms / 1000));
  if (total < 60) return 'Just now';
  const m = Math.floor(total / 60);
  if (m < 60) return `${m}m ago`;
  return `${Math.floor(m / 60)}h ${String(m % 60).padStart(2, '0')}m ago`;
}

function NoticeIcon({ value, alt }: { value: string; alt: string }) {
  const [failed, setFailed] = useState(false);
  const isImage = value.startsWith('/');

  if (isImage && !failed) {
    return <img src={value} alt={alt} onError={() => setFailed(true)} />;
  }
  // Not an image, or the image failed to load: show the first letter instead
  return <span>{isImage ? alt[0] : value}</span>;
}

export default function NotificationsPanel() {
  const { user } = useAuth();
  const { now, notices, dispatches, selectedNoticeId, selectNotice, addTestNotice } =
    useDispatchStore();
  const canManage = canManageDispatch(user.role);

  // Severity first, then oldest first
  const sorted = [...notices].sort((a, b) => a.severity - b.severity || a.createdAt - b.createdAt);
  const criticalCount = notices.filter((n) => n.severity === 1).length;

  return (
    <section className="panel panel-notices" aria-label="Notifications">
      <header className="panel-header">
        <h2>
          Notifications ({notices.length})
          {criticalCount > 0 && <span className="critical-count">{criticalCount} critical</span>}
        </h2>
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
              <li
                key={n.id}
                className={`notice sev-${n.severity} type-${n.incidentType}${selected ? ' selected' : ''}`}
              >
                <div className="notice-icon" aria-hidden="true">
                  <NoticeIcon value={TYPE_ICON[n.incidentType]} alt={n.incidentType} />
                </div>
                <div className="notice-body">
                  <div className="notice-top">
                    <span className="sev-pill">{SEVERITY_LABEL[n.severity]}</span>
                    <span className="notice-type">{n.incidentType}</span>
                    <span className="notice-age">{formatAge(now - n.createdAt)}</span>
                  </div>

                  <h3>{n.title}</h3>
                  <p className="notice-loc">📍 {n.locationLabel}</p>

                  <div className="notice-chips">
                    <span className="chip chip-need">
                      {UNIT_ICON[n.requiredUnitType]} Needs {n.requiredUnitType}
                    </span>
                    <span className={`chip ${responding > 0 ? 'chip-ok' : 'chip-alert'}`}>
                      {responding > 0 ? `${responding} responding` : 'No unit sent'}
                    </span>
                  </div>

                  {canManage && (
                    <button
                      className={`notice-btn${selected ? ' active' : ''}`}
                      onClick={() => selectNotice(selected ? null : n.id)}
                    >
                      {selected ? '✓ Destination set' : 'Set as destination'}
                    </button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}