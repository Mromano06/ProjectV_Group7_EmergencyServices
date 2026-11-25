import AvailableUnits from './components/AvailableUnitPanel';
import DispatchTracker from './components/DispatchTracker';
import NotificationsPanel from './components/NotificationPanel';
import LogPanel from './components/LogPanel';
import BudgetPanel from './components/BudgetPanel';
import TopBar from './components/TopBar';
import './App.css'

function App() {
  return (
    <main className="app">
      {/* Connection status, signed-in user and active weather warnings (ES-FR-022, issue #21) */}
      <TopBar />

      <section className="dashboard">
          <h2 className="visually-hidden">Emergency Services Dashboard</h2>

          <AvailableUnits />

          <DispatchTracker />

          <NotificationsPanel />

          <LogPanel />

          <BudgetPanel />
        </section>
    </main>
  )
}

export default App
