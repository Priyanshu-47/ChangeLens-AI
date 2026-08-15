import { useEffect, useRef, useState } from 'react';
import { analysesApi } from '../api/endpoints';
import type { AnalysisRun } from '../api/types';

export interface PollingState {
  run: AnalysisRun | null;
  loading: boolean;
  error: string | null;
  /** True while the analysis is still Queued/Running (the hook keeps polling). */
  active: boolean;
  /** True after a terminal state (Succeeded/Failed) or a fetch error. */
  settled: boolean;
}

/**
 * Polls GET /api/v1/analyses/{id} only while the job is Queued/Running; stops at
 * Succeeded/Failed; cleans up the timer on unmount (brief §12, §35). The interval
 * is 2.5s — never aggressive.
 */
export function useAnalysisPolling(analysisId: string | undefined, intervalMs = 2500): PollingState {
  const [run, setRun] = useState<AnalysisRun | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [settled, setSettled] = useState(false);
  const runningRef = useRef(false);

  useEffect(() => {
    if (!analysisId) {
      setLoading(false);
      setSettled(true);
      return;
    }

    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | null = null;

    const poll = async () => {
      if (runningRef.current) {
        return;
      }
      runningRef.current = true;
      try {
        const current = await analysesApi.get(analysisId!);
        if (cancelled) {
          return;
        }
        setRun(current);
        setError(null);
        setLoading(false);

        const terminal = current.status === 'Succeeded' || current.status === 'Failed';
        if (terminal) {
          setSettled(true);
          return;
        }
        timer = setTimeout(poll, intervalMs);
      } catch (e) {
        if (cancelled) {
          return;
        }
        setError(e instanceof Error ? e.message : 'Failed to load the analysis.');
        setLoading(false);
        setSettled(true); // surface the error; don't poll forever
      } finally {
        runningRef.current = false;
      }
    };

    void poll();

    return () => {
      cancelled = true;
      if (timer !== null) {
        clearTimeout(timer);
      }
    };
  }, [analysisId, intervalMs]);

  return {
    run,
    loading,
    error,
    active: !settled && run !== null && (run.status === 'Queued' || run.status === 'Running'),
    settled,
  };
}
