import { useAuth } from './auth/AuthContext';
import AvailableUnits from './components/AvailableUnitPanel';
import DispatchTracker from './components/DispatchTracker';
import NotificationsPanel from './components/NotificationPanel';
import LogPanel from './components/LogPanel';
import type { Role } from './types/models';

const ROLES: Role[] = ['Dispatcher', 'Admin', 'Viewer'];

function App() {
  const { user, setRole } = useAuth();

  return (
    <>
      <div className="role-switch">
        <label>
          Mock role:{' '}
          <select value={user.role} onChange={(e) => setRole(e.target.value as Role)}>
            {ROLES.map((r) => <option key={r}>{r}</option>)}
          </select>
        </label>
      </div>
      <NotificationsPanel />
      <AvailableUnits />
      <LogPanel />
      <DispatchTracker />
    </>
  );
}
export default App;