import { useState, type FormEvent } from 'react';
import { login, loginErrorMessage } from '../api/authApi';
import { useAuth } from '../auth/AuthContext';
import '../styles/Login.css';

// SH-S01: sign in with a city account from Registration. Our API checks the username and
// password with Registration and gives back our own token; the password is never stored.

export default function LoginScreen() {
  const { signIn, notice } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>(undefined);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (busy) return;
    if (username.trim() === '' || password === '') {
      setError('Enter your username and password.');
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      const result = await login(username.trim(), password);
      setPassword('');
      signIn(result.accessToken);
    } catch (err) {
      setError(loginErrorMessage(err));
      setPassword('');
      setBusy(false);
    }
  }

  return (
    <main className="login-page">
      <form className="login-card" onSubmit={submit} noValidate aria-labelledby="login-title">
        <h1 id="login-title">Emergency Services</h1>
        <p className="login-sub">Sign in with your city account (Registration).</p>

        {notice && !error && <p className="login-notice" role="status">{notice}</p>}
        {error && <p className="login-error" role="alert">{error}</p>}

        <label htmlFor="login-username">Username</label>
        <input
          id="login-username"
          name="username"
          autoComplete="username"
          autoFocus
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          disabled={busy}
        />

        <label htmlFor="login-password">Password</label>
        <input
          id="login-password"
          name="password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          disabled={busy}
        />

        <button type="submit" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>

        <p className="login-help">
          Test accounts only – don't use real personal information.
          Managers get full access, Dispatchers can dispatch, Users can view.
        </p>
      </form>
    </main>
  );
}
