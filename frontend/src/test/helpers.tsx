import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

export interface MockRoute {
  method?: string;
  /** Matched against url.pathname (which includes the /api/v1 prefix). */
  path: string | RegExp;
  /** Static body, or an array consumed in order per call to simulate progression. */
  body?: unknown | unknown[];
  status?: number;
}

/**
 * Stubs global fetch with a path/method router. `body` may be an array, in which
 * case each successive call to that route returns the next element (used to
 * simulate Queued → Running → Succeeded polling progressions).
 */
export function installFetchMock(routes: MockRoute[]) {
  const counters = new Map<MockRoute, number>();
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? new URL(input) : input instanceof URL ? input : new URL(String(input));
    const method = init?.method ?? 'GET';
    const route = routes.find((r) => {
      const methodOk = r.method === undefined || r.method === method;
      const pathOk = typeof r.path === 'string' ? url.pathname === r.path : r.path.test(url.pathname);
      return methodOk && pathOk;
    });
    if (!route) {
      throw new Error(`Unmocked request: ${method} ${url.pathname}${url.search}`);
    }
    const attempt = counters.get(route) ?? 0;
    counters.set(route, attempt + 1);
    const body = Array.isArray(route.body) ? route.body[Math.min(attempt, route.body.length - 1)] : route.body;
    return jsonResponse(body ?? {}, route.status ?? 200);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

/** Renders a single component at a route inside MemoryRouter (no navigation asserted). */
export function renderAt(ui: ReactNode, route = '/') {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <Routes>
        <Route path="*" element={ui} />
      </Routes>
    </MemoryRouter>,
  );
}
