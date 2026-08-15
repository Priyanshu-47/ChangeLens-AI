import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider, ProtectedRoute, useAuth } from './AuthContext';
import { LoginPage } from '../pages/LoginPage';
import { tokenStore } from '../api/client';
import { installFetchMock } from '../test/helpers';
import type { AuthResponse, MeResponse } from '../api/types';

const authResponse: AuthResponse = {
  accessToken: 'jwt-abc',
  expiresInSeconds: 3600,
  tokenType: 'Bearer',
  user: { id: 'u1', email: 'engineer@changelens.dev', displayName: 'Engineer', roles: ['Engineer'] },
};

const meResponse: MeResponse = {
  user: authResponse.user,
  memberships: [{ projectId: 'p1', projectName: 'AcmePay', role: 'Engineer' }],
};

function renderLogin() {
  return render(
    <MemoryRouter initialEntries={['/login']}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<div>Home after login</div>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('auth', () => {
  it('logs in, stores the token, and navigates to the destination', async () => {
    const user = userEvent.setup();
    installFetchMock([
      { method: 'POST', path: '/api/v1/auth/login', body: authResponse },
      { path: '/api/v1/auth/me', body: meResponse },
    ]);
    renderLogin();

    await user.type(screen.getByLabelText('Email'), 'engineer@changelens.dev');
    await user.type(screen.getByLabelText('Password'), 'EngineerPass!2026');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen.getByText('Home after login')).toBeInTheDocument());
    expect(tokenStore.get()).toBe('jwt-abc');
  });

  it('shows a friendly error on invalid credentials (401)', async () => {
    const user = userEvent.setup();
    installFetchMock([
      {
        method: 'POST',
        path: '/api/v1/auth/login',
        status: 401,
        body: { type: 'problem', title: 'Invalid credentials', status: 401, code: 'invalid_credentials' },
      },
    ]);
    renderLogin();

    await user.type(screen.getByLabelText('Email'), 'engineer@changelens.dev');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen.getByText('Invalid email or password.')).toBeInTheDocument());
    expect(tokenStore.get()).toBeNull();
  });

  it('shows a friendly error when the API is unreachable', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }));
    renderLogin();

    await user.type(screen.getByLabelText('Email'), 'engineer@changelens.dev');
    await user.type(screen.getByLabelText('Password'), 'EngineerPass!2026');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen.getByText('Cannot reach the API. Check that the backend is running.')).toBeInTheDocument());
  });

  it('redirects unauthenticated visitors from a protected route to /login', async () => {
    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<div>Login screen</div>} />
            <Route
              path="/dashboard"
              element={
                <ProtectedRoute>
                  <div>Dashboard content</div>
                </ProtectedRoute>
              }
            />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );
    expect(await screen.findByText('Login screen')).toBeInTheDocument();
    expect(screen.queryByText('Dashboard content')).not.toBeInTheDocument();
  });

  it('restores a valid session from the stored token and shows the protected content', async () => {
    tokenStore.set('jwt-abc');
    installFetchMock([{ path: '/api/v1/auth/me', body: meResponse }]);
    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<div>Login screen</div>} />
            <Route
              path="/dashboard"
              element={
                <ProtectedRoute>
                  <div>Dashboard content</div>
                </ProtectedRoute>
              }
            />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );
    expect(await screen.findByText('Dashboard content')).toBeInTheDocument();
  });

  it('clears the session and returns to login on logout', async () => {
    const user = userEvent.setup();
    tokenStore.set('jwt-abc');
    installFetchMock([{ path: '/api/v1/auth/me', body: meResponse }]);

    function LogoutHarness() {
      const { user: current, logout } = useAuth();
      return (
        <div>
          <span data-testid="user-email">{current?.email}</span>
          <button type="button" onClick={logout}>Log out</button>
        </div>
      );
    }

    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<div>Login screen</div>} />
            <Route
              path="/dashboard"
              element={
                <ProtectedRoute>
                  <LogoutHarness />
                </ProtectedRoute>
              }
            />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByTestId('user-email')).toHaveTextContent('engineer@changelens.dev'));
    await user.click(screen.getByRole('button', { name: 'Log out' }));

    expect(tokenStore.get()).toBeNull();
    expect(await screen.findByText('Login screen')).toBeInTheDocument();
  });
});
