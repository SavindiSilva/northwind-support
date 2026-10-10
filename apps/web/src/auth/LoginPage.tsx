import { useState, type FormEvent } from 'react';
import {
  Navigate,
  useLocation,
  useNavigate,
} from 'react-router-dom';
import { ApiError } from '../api/client';
import { useAuth } from './AuthProvider';

interface LoginLocationState {
  from?: {
    pathname?: string;
    search?: string;
    hash?: string;
  };
}

export function LoginPage() {
  const { user, loading, login } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');

  const state = location.state as LoginLocationState | null;
  const destination = state?.from;
  const returnTo = destination?.pathname?.startsWith('/') &&
    !destination.pathname.startsWith('//')
    ? `${destination.pathname}${destination.search ?? ''}${destination.hash ?? ''}`
    : '/tickets';

  if (loading) {
    return <p role="status">Checking your session...</p>;
  }

  if (user) {
    return <Navigate to={returnTo} replace />;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const trimmedEmail = email.trim();

    if (!trimmedEmail || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
      setError('Enter a valid email address.');
      return;
    }

    if (!password) {
      setError('Enter your password.');
      return;
    }

    setPending(true);
    setError('');

    try {
      await login(trimmedEmail, password);
      navigate(returnTo, { replace: true });
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.status === 401
          ? 'Invalid email or password, or this account is inactive.'
          : caught instanceof Error
            ? caught.message
            : 'Unable to sign in. Please try again.',
      );
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="page">
      <section className="card" aria-labelledby="login-heading">
        <h1 id="login-heading">Sign in</h1>
        <p>Sign in to access Northwind Support.</p>

        <form className="form" onSubmit={handleSubmit}>
          <div className="field">
            <label className="field__label" htmlFor="login-email">
              Email
            </label>
            <input
              id="login-email"
              name="email"
              type="email"
              autoComplete="username"
              className="field__input"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
              disabled={pending}
            />
          </div>

          <div className="field">
            <label className="field__label" htmlFor="login-password">
              Password
            </label>
            <input
              id="login-password"
              name="password"
              type="password"
              autoComplete="current-password"
              className="field__input"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
              disabled={pending}
            />
          </div>

          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}

          <button
            className="button button--primary"
            type="submit"
            disabled={pending}
          >
            {pending ? 'Signing in...' : 'Sign in'}
          </button>
        </form>
      </section>
    </div>
  );
}
