// Centralized HTTP client: base URL, bearer auth, correlation ids, and uniform
// error mapping. No component duplicates fetch configuration. The API base URL is
// public config (VITE_*) — never put secrets here.

import { ApiError, type ApiErrorEnvelope } from './types';

export const API_BASE_URL: string =
  (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:5000/api/v1';

const TOKEN_STORAGE_KEY = 'changelens.accessToken';
// requestId -> correlationId; only the last N are kept (debug aid).
const RECENT_IDS: { ts: string; correlationId: string; path: string }[] = [];
const MAX_RECENT_IDS = 20;

export const tokenStore = {
  get(): string | null {
    return window.localStorage.getItem(TOKEN_STORAGE_KEY);
  },
  set(token: string): void {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, token);
  },
  clear(): void {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  },
};

let lastCorrelationId: string | null = null;

/** Correlation id for the current session's most recent request (display only). */
export function currentCorrelationId(): string | null {
  return lastCorrelationId;
}

export function recentRequestIds(): typeof RECENT_IDS {
  return [...RECENT_IDS].reverse();
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PATCH' | 'DELETE';
  body?: unknown;
  /** Set to false to skip the Authorization header (e.g. login). */
  auth?: boolean;
  query?: Record<string, string | number | boolean | null | undefined>;
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, auth = true, query } = options;

  const correlationId = crypto.randomUUID();
  lastCorrelationId = correlationId;

  const url = new URL(`${API_BASE_URL}${path}`);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== '') {
        url.searchParams.set(key, String(value));
      }
    }
  }

  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Correlation-ID': correlationId,
  };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  const token = tokenStore.get();
  if (auth && token) {
    headers.Authorization = `Bearer ${token}`;
  }

  RECENT_IDS.push({ ts: new Date().toISOString(), correlationId, path: `${method} ${path}` });
  if (RECENT_IDS.length > MAX_RECENT_IDS) {
    RECENT_IDS.splice(0, RECENT_IDS.length - MAX_RECENT_IDS);
  }

  let response: Response;
  try {
    response = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError('Network error — the API is unreachable.', 0, 'network_error', correlationId, null);
  }

  if (response.ok) {
    if (response.status === 204) {
      return undefined as T;
    }
    return (await response.json()) as T;
  }

  let envelope: ApiErrorEnvelope | null = null;
  try {
    envelope = (await response.json()) as ApiErrorEnvelope;
  } catch {
    // non-JSON error body — fall back to the generic mapping
  }

  const message = envelope?.detail || envelope?.title || `Request failed (HTTP ${response.status}).`;
  throw new ApiError(
    message,
    response.status,
    envelope?.code || httpStatusFallback(response.status),
    envelope?.traceId ?? correlationId,
    envelope?.details ?? null,
  );
}

function httpStatusFallback(status: number): string {
  if (status === 401) return 'unauthorized';
  if (status === 403) return 'forbidden';
  if (status === 404) return 'not_found';
  if (status === 422) return 'ai_validation_failed';
  if (status === 429) return 'rate_limited';
  return `http_${status}`;
}
