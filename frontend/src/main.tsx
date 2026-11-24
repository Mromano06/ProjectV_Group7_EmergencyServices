import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'
import { DispatchProvider } from './state/DispatchContext.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider>
      <DispatchProvider>
        <App />
      </DispatchProvider>
    </AuthProvider>
  </StrictMode>,
)