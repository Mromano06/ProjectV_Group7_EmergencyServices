import type { ReactNode } from 'react';
import LoginScreen from '../components/LoginScreen';
import { DispatchProvider } from '../state/DispatchContext';
import { useAuth } from './AuthContext';

/**
 * Shows the login screen until someone signs in, then the console.
 * The console (and its 5-second polling) only starts after sign-in, and is thrown away on sign-out,
 * so the next person never sees the last person's data.
 */
export default function AuthGate({ children }: { children: ReactNode }) {
  const { signedIn } = useAuth();
  return signedIn ? <DispatchProvider>{children}</DispatchProvider> : <LoginScreen />;
}
