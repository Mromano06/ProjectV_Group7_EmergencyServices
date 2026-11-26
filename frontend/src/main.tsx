import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'
import { DispatchProvider } from './state/DispatchContext.tsx'

/**
 * @file main.tsx
 * @brief Starts the React app and renders it with shared auth and dispatch state.
 */

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider>
      <DispatchProvider>
        <App />
      </DispatchProvider>
    </AuthProvider>
  </StrictMode>,
)