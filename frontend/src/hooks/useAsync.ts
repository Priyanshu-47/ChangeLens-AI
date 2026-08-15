import { useCallback, useEffect, useRef, useState } from 'react';

interface AsyncState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
  /** (Re)run the loader. */
  run: () => Promise<void>;
}

/**
 * Minimal async-state helper: every API call gets loading / success / error states
 * (brief §28). `deps` re-triggers the loader; cleanup-safe (stale results are dropped).
 */
export function useAsync<T>(loader: () => Promise<T>, deps: unknown[] = []): AsyncState<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const runIdRef = useRef(0);

  const run = useCallback(async () => {
    const runId = ++runIdRef.current;
    setLoading(true);
    setError(null);
    try {
      const result = await loader();
      if (runId === runIdRef.current) {
        setData(result);
      }
    } catch (e) {
      if (runId === runIdRef.current) {
        setError(e instanceof Error ? e.message : 'Request failed.');
      }
    } finally {
      if (runId === runIdRef.current) {
        setLoading(false);
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  useEffect(() => {
    void run();
    return () => {
      runIdRef.current++;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  return { data, loading, error, run };
}
