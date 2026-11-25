import { useAuth } from './auth/AuthContext';
import AvailableUnits from './components/AvailableUnitPanel';
import DispatchTracker from './components/DispatchTracker';
import NotificationsPanel from './components/NotificationPanel';
import LogPanel from './components/LogPanel';
import { useDispatchStore, type Connection } from './state/DispatchContext';
import { parseUtc } from './utils/Labels';
import './App.css'

const CONNECTION_LABEL: Record<Connection, string> = {
  connecting: 'Connecting to the API…',
  online: 'Live – updates every 5 seconds',
  offline: 'Offline – retrying',
};

function App() {
  const { user } = useAuth();
  const { warnings, warningsState, warningsError, connection } = useDispatchStore();

  return (
    <main className="app">
      <h1>Emergency Services</h1>
      <p className={`message connection-${connection}`} role="status">
        {CONNECTION_LABEL[connection]} · Signed in as {user.name} ({user.role})
      </p>

      <h2>Active Weather Warnings</h2>

      {warningsState === 'loading' ? (
        <p className="message">Loading weather warnings...</p>
      ) : warningsState === 'error' ? (
        <p className="message error">
          Could not load warnings: {warningsError}
        </p>
      ) : warnings.length === 0 ? (
        <p className="message">No active warnings.</p>
      ) : (
        <section className="warning-list">
          {warnings.map((warning) => (
            <article className="warning-card" key={warning.id}>
              <h3>{warning.type}</h3>

              <p>
                <strong>Severity:</strong> {warning.severity}
              </p>

              <p>
                <strong>Area:</strong> {warning.area}
              </p>

              <p>
                <strong>Status:</strong> {warning.status}
              </p>

              {warning.description && (
                <p>
                  <strong>Details:</strong> {warning.description}
                </p>
              )}

              <p>
                <strong>Ends:</strong>{' '}
                {parseUtc(warning.endsAt).toLocaleString()}
              </p>

              {warning.source && (
                <p>
                  <strong>Source:</strong> {warning.source}
                </p>
              )}

              {warning.affectsTransit && (
                <p className="transit-warning">
                  This warning affects transit.
                </p>
              )}
            </article>
          ))}
        </section>
      )}

      <section className="dashboard">
          <h2>Emergency Services Dashboard</h2>

          <AvailableUnits />

          <DispatchTracker />

          <NotificationsPanel />

          <LogPanel />
        </section>
    </main>
  )
}

export default App
