import { api, tokenStore, currentCorrelationId } from './client';
import { ApiError } from './types';
import { installFetchMock } from '../test/helpers';

describe('api client', () => {
  it('sends bearer token, correlation id, and JSON content type', async () => {
    tokenStore.set('tok-123');
    const fetchMock = installFetchMock([
      {
        method: 'POST',
        path: '/api/v1/analyses/change-risk',
        body: { ok: true },
      },
    ]);

    await api('/analyses/change-risk', { method: 'POST', body: { changeSummary: 'x' } });

    const [input, init] = fetchMock.mock.calls[0] as unknown as [RequestInfo | URL, RequestInit];
    const url = new URL(String(input));
    const headers = init.headers as Record<string, string>;
    expect(url.pathname).toBe('/api/v1/analyses/change-risk');
    expect(headers.Authorization).toBe('Bearer tok-123');
    expect(headers['Content-Type']).toBe('application/json');
    expect(headers['X-Correlation-ID']).toBeTruthy();
    expect(JSON.parse(String(init.body))).toEqual({ changeSummary: 'x' });
  });

  it('omits the Authorization header for auth:false requests (login)', async () => {
    const fetchMock = installFetchMock([{ method: 'POST', path: '/api/v1/auth/login', body: { ok: true } }]);
    await api('/auth/login', { method: 'POST', body: { email: 'a@b.c', password: 'p' }, auth: false });
    const [, init] = fetchMock.mock.calls[0] as unknown as [RequestInfo | URL, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBeUndefined();
  });

  it('maps a 401 envelope to an ApiError with code and trace id', async () => {
    installFetchMock([
      {
        method: 'POST',
        path: '/api/v1/auth/login',
        status: 401,
        body: { type: 'problem', title: 'Invalid credentials', status: 401, code: 'invalid_credentials', traceId: 'tr-1' },
      },
    ]);

    await expect(api('/auth/login', { method: 'POST', body: {}, auth: false })).rejects.toMatchObject({
      name: 'ApiError',
      status: 401,
      code: 'invalid_credentials',
      traceId: 'tr-1',
    } as Partial<ApiError>);
  });

  it('maps a network failure to a network_error ApiError', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }));
    await expect(api('/projects')).rejects.toMatchObject({
      status: 0,
      code: 'network_error',
    } as Partial<ApiError>);
  });

  it('exposes the correlation id of the most recent request', async () => {
    installFetchMock([{ path: '/api/v1/projects', body: { items: [] } }]);
    await api('/projects');
    expect(currentCorrelationId()).toBeTruthy();
  });
});
