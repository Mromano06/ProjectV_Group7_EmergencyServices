import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'
import AuthGate from './auth/AuthGate'

/**
 * @file main.tsx
 * @brief Starts the React app and renders it with shared auth and dispatch state.
 */

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider>
      {/* Login screen until someone signs in (SH-S01), then the console */}
      <AuthGate>
        <App />
      </AuthGate>
    </AuthProvider>
  </StrictMode>,
)