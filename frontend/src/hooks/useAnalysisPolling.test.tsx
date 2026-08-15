import { screen, waitFor } from '@testing-library/react';
import { useAnalysisPolling } from './useAnalysisPolling';
import { installFetchMock, renderAt } from '../test/helpers';
import type { AnalysisRun } from '../api/types';

const base: AnalysisRun = {
  id: 'an-1',
  projectId: 'p1',
  type: 'IncidentInvestigation',
  status: 'Queued',
  incidentId: 'inc-1',
  result: null,
  resultSchemaVersion: null,
  model: null,
  promptVersion: null,
  queuedAtUtc: '2026-08-01T10:00:00Z',
  startedAtUtc: null,
  completedAtUtc: null,
  error: null,
};

function Harness({ id, interval }: { id: string; interval: number }) {
  const { run, loading, active, settled, error } = useAnalysisPolling(id, interval);
  return (
    <div>
      <div data-testid="status">{run?.status ?? 'none'}</div>
      <div data-testid="loading">{String(loading)}</div>
      <div data-testid="active">{String(active)}</div>
      <div data-testid="settled">{String(settled)}</div>
      <div data-testid="error">{error ?? ''}</div>
    </div>
  );
}

describe('useAnalysisPolling', () => {
  it('polls Queued → Running → Succeeded and stops at the terminal state', async () => {
    installFetchMock([
      {
        method: 'GET',
        path: /\/api\/v1\/analyses\/an-1$/,
        body: [
          { ...base, status: 'Queued' },
          { ...base, status: 'Running', startedAtUtc: '2026-08-01T10:00:01Z' },
          {
            ...base,
            status: 'Succeeded',
            startedAtUtc: '2026-08-01T10:00:01Z',
            completedAtUtc: '2026-08-01T10:00:30Z',
            resultSchemaVersion: 'incident-v1',
            result: {
              rootCauseCandidates: [],
              remediation: { immediateMitigation: null, investigationSteps: [], recommendedRemediation: null, validationSteps: [], rollbackConsideration: null, insufficientEvidence: true },
              unknowns: [],
              evidence: [],
            },
          },
        ],
      },
    ]);

    renderAt(<Harness id="an-1" interval={20} />);

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('Succeeded'));
    expect(screen.getByTestId('active')).toHaveTextContent('false');
    expect(screen.getByTestId('settled')).toHaveTextContent('true');
    expect(screen.getByTestId('loading')).toHaveTextContent('false');
  });

  it('stops polling and surfaces the error when the fetch fails', async () => {
    installFetchMock([
      {
        method: 'GET',
        path: /\/api\/v1\/analyses\/an-1$/,
        status: 500,
        body: { type: 'problem', title: 'Internal error', status: 500, code: 'internal' },
      },
    ]);

    renderAt(<Harness id="an-1" interval={20} />);

    await waitFor(() => expect(screen.getByTestId('settled')).toHaveTextContent('true'));
    expect(screen.getByTestId('error')).not.toHaveTextContent('');
    expect(screen.getByTestId('status')).toHaveTextContent('none');
  });
});
