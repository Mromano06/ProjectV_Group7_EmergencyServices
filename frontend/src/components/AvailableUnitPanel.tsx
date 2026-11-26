/**
 * @file AvailableUnitPanel.tsx
 * @brief Lists available units and lets authorized users select one for a call.
 *
 * A mismatched unit can still be dispatched, but the call stays open until
 * the required unit type arrives.
 */

import { useAuth } from '../auth/AuthContext';
import { canManageDispatch } from '../auth/Permissions';
import { useDispatchStore } from '../state/DispatchContext';
import UnitBadge from '../components/UnitBadge';
import '../styles/Panels.css';
/**
 * Lists active, available units and lets dispatch-capable users select one for a call.
 * A unit of the wrong type can still be sent, but the call stays open until the
 * required unit type arrives.
 */
export default function AvailableUnits() {
  const { user } = useAuth();
  const { units, notices, selectedNoticeId, selectedUnitId, selectUnit, sendSelected, busy } =
    useDispatchStore();
  const canManage = canManageDispatch(user.role);

  const available = units
    .filter((u) => u.active && u.status === 'Available')
    .sort((a, b) => a.type.localeCompare(b.type) || a.callSign.localeCompare(b.callSign));

  const notice = notices.find((n) => n.id === selectedNoticeId) ?? null;
  const selectedUnit = available.find((u) => u.id === selectedUnitId) ?? null;
  const canSend = canManage && !busy && notice !== null && selectedUnit !== null;
  const mismatch = notice && selectedUnit && selectedUnit.type !== notice.requiredUnitType;

  return (
    <section className="panel panel-available" aria-label="Available units">
      <header className="panel-header">
        <h2>Available units ({available.length})</h2>
      </header>

      <div className={`destination${notice ? '' : ' destination-empty'}`}>
        <span className="destination-label">Destination</span>
        {notice ? (
          <>
            <strong>{notice.locationLabel}</strong>
            <span>{notice.title} · needs {notice.requiredUnitType}</span>
          </>
        ) : (
          <span>Pick a call in Notifications to set it.</span>
        )}
      </div>

      {available.length === 0 ? (
        <p className="panel-empty">No units available.</p>
      ) : (
        <ul className="panel-list">
          {available.map((u) => {
            const selected = u.id === selectedUnitId;
            const match = notice !== null && u.type === notice.requiredUnitType;
            return (
              <li key={u.id}>
                <button
                  className={`unit-item${selected ? ' selected' : ''}`}
                  disabled={!canManage}
                  aria-pressed={selected}
                  onClick={() => selectUnit(selected ? null : u.id)}
                >
                  <UnitBadge type={u.type} />
                  <span className="unit-text">
                    <span className="unit-name">{u.callSign}</span>
                    <span className="unit-sub">{u.type} · {u.homeStation}</span>
                  </span>
                  {match && <span className="tag-match">Matches call</span>}
                </button>
              </li>
            );
          })}
        </ul>
      )}

      {mismatch && notice && selectedUnit && (
        <p className="warn">
          This call needs an {notice.requiredUnitType}. {selectedUnit.callSign} can go, but the
          call stays open until the right unit arrives.
        </p>
      )}

      {canManage && (
        <footer className="panel-footer">
          <button className="send-btn" disabled={!canSend} onClick={() => void sendSelected()}>
            {busy ? 'Sending…' : selectedUnit ? `Send ${selectedUnit.callSign}` : 'Send'}
          </button>
        </footer>
      )}
    </section>
  );
}