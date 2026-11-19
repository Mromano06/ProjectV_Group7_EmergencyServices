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
      <select
        aria-label="Role"
        className="role-switch"
        value={user.role}
        onChange={(event) => setRole(event.target.value as Role)}
      >
        {ROLES.map((role) => (
          <option key={role} value={role}>
            {role}
          </option>
        ))}
      </select>

      <NotificationsPanel />
      <AvailableUnits />
      <DispatchTracker />
      <LogPanel />
    </>
  );
}

export default App;