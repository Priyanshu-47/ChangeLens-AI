import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/types';

interface LocationState {
  from?: string;
}

export function LoginPage() {
  const { user, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as LocationState | null)?.from ?? '/';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (user) {
    return <Navigate to={from} replace />;
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email.trim(), password);
      navigate(from, { replace: true });
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 401) {
          setError('Invalid email or password.');
        } else if (err.status === 0) {
          setError('Cannot reach the API. Check that the backend is running.');
        } else {
          setError(err.message);
        }
      } else {
        setError('Login failed. Please try again.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-panel">
        <div className="brand" style={{ border: 'none', padding: '0 0 24px' }}>
          <div className="brand-mark" aria-hidden="true" style={{ background: 'var(--accent)' }}>C</div>
          <div>
            <div className="brand-name" style={{ color: 'var(--text)' }}>ChangeLens AI</div>
            <div className="brand-sub">Sign in to continue</div>
          </div>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="field">
            <label htmlFor="email">Email</label>
            <input
              id="email"
              className="input"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          </div>
          <div className="field">
            <label htmlFor="password">Password</label>
            <input
              id="password"
              className="input"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </div>

          {error ? <div className="form-error" role="alert">{error}</div> : null}

          <button type="submit" className="btn btn-primary" disabled={submitting} style={{ width: '100%' }}>
            {submitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        <div className="demo-hint">
          <strong>Demo accounts</strong> (dev seed): engineer@changelens.dev / EngineerPass!2026 ·
          viewer@changelens.dev / ViewerPass!2026 · admin@changelens.dev / AdminPass!2026
        </div>
      </div>

      <div className="login-hero">
        <h1>What changed. What could break. What does the evidence say.</h1>
        <p>
          ChangeLens turns code changes and incidents into grounded, evidence-linked
          investigations — before you ship and after something breaks.
        </p>
        <div className="hero-flow">
          <div>change / incident</div>
          <div>&nbsp;&nbsp;↓&nbsp; hybrid retrieval (vector + keyword + dependency)</div>
          <div>&nbsp;&nbsp;↓&nbsp; structured AI analysis</div>
          <div>&nbsp;&nbsp;↓&nbsp; grounded result · evidence ids · unknowns</div>
        </div>
      </div>
    </div>
  );
}
