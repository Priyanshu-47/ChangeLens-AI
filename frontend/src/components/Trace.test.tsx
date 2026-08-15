import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TraceSection } from './Trace';
import { installFetchMock } from '../test/helpers';
import type { AnalysisTrace } from '../api/types';

const traceFixture: AnalysisTrace = {
  analysisId: 'an-1',
  type: 'IncidentInvestigation',
  status: 'Succeeded',
  model: 'mock',
  promptVersion: 'incident-v1',
  resultSchemaVersion: 'incident-v1',
  traceSchemaVersion: 'trace-v1',
  stages: [
    { name: 'Context', status: 'Completed', startedAtUtc: null, completedAtUtc: null, durationMs: 12, metadata: null },
    { name: 'AI Analysis', status: 'Completed', startedAtUtc: null, completedAtUtc: null, durationMs: 95, metadata: null },
    { name: 'Persistence', status: 'Completed', startedAtUtc: null, completedAtUtc: null, durationMs: 8, metadata: null },
  ],
  retrieval: {
    queries: ['HTTP 401 after JWT signing-key rotation'],
    candidateCount: 4,
    selectedCount: 2,
    maxChunks: 20,
    maxCharsPerChunk: 12000,
    items: [
      { id: 'chunk:abc', documentType: 'Runbook', title: null, path: 'auth-001-jwt-key-rotation.md', score: 0.9, vectorScore: 0.88, keywordRank: 1, dependencyRank: null },
      { id: 'chunk:def', documentType: 'SourceCode', title: null, path: 'src/Auth/TokenService.cs', score: 0.8, vectorScore: null, keywordRank: 2, dependencyRank: 1 },
    ],
  },
  failureCode: null,
  failureCategory: null,
};

function mockTrace(body: unknown, status = 200) {
  installFetchMock([{ method: 'GET', path: /\/api\/v1\/analyses\/an-1\/trace$/, body, status }]);
}

describe('TraceSection', () => {
  it('renders the stage timeline and retrieval explorer on expand', async () => {
    const user = userEvent.setup();
    mockTrace(traceFixture);
    render(<TraceSection analysisId="an-1" status="Succeeded" />);

    // Lazy: nothing fetched until expanded.
    expect(screen.queryByText('Context')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Show trace' }));

    expect(await screen.findByText('Per-stage timing')).toBeInTheDocument();
    expect(screen.getByText('Context')).toBeInTheDocument();
    expect(screen.getByText('AI Analysis')).toBeInTheDocument();
    expect(screen.getByText('95 ms')).toBeInTheDocument();
    expect(screen.getByText('12 ms')).toBeInTheDocument();

    // Retrieval explorer: candidate/selected counts and per-item leg attribution.
    expect(screen.getByText(/4 candidates → 2 selected/)).toBeInTheDocument();
    expect(screen.getByText('vector 0.880')).toBeInTheDocument();
    expect(screen.getByText('keyword #1')).toBeInTheDocument();
    expect(screen.getByText('dependency #1')).toBeInTheDocument();
    expect(screen.getByText(/not directly comparable/)).toBeInTheDocument();
    expect(screen.getByText('auth-001-jwt-key-rotation.md')).toBeInTheDocument();
  });

  it('renders a failed stage with its failure category and code', async () => {
    const user = userEvent.setup();
    mockTrace({
      ...traceFixture,
      status: 'Failed',
      failureCode: 'AI_UNAVAILABLE',
      failureCategory: 'AI_PROVIDER',
      stages: [
        { name: 'Context', status: 'Completed', startedAtUtc: null, completedAtUtc: null, durationMs: 10, metadata: null },
        {
          name: 'AI Analysis',
          status: 'Failed',
          startedAtUtc: null,
          completedAtUtc: null,
          durationMs: 40,
          metadata: { failureCode: 'AI_UNAVAILABLE', failureCategory: 'AI_PROVIDER' },
        },
      ],
      retrieval: null,
    });
    render(<TraceSection analysisId="an-1" status="Failed" />);

    await user.click(screen.getByRole('button', { name: 'Show trace' }));

    await waitFor(() => expect(screen.getAllByText('AI_PROVIDER').length).toBeGreaterThan(0));
    expect(screen.getAllByText('AI_UNAVAILABLE').length).toBeGreaterThan(0);
    expect(screen.getByText(/category AI_PROVIDER/)).toBeInTheDocument();
  });

  it('shows the in-progress hint while the analysis is still running', async () => {
    const user = userEvent.setup();
    mockTrace({ ...traceFixture, status: 'Running', stages: [], retrieval: null });
    render(<TraceSection analysisId="an-1" status="Running" />);

    await user.click(screen.getByRole('button', { name: 'Show trace' }));

    expect(await screen.findByText(/The trace is written when the analysis completes/)).toBeInTheDocument();
    expect(screen.queryByText('Context')).not.toBeInTheDocument();
  });

  it('surfaces a friendly error state when the trace cannot be loaded', async () => {
    const user = userEvent.setup();
    mockTrace({ type: 'problem', title: 'Not found', status: 404, code: 'not_found' }, 404);
    render(<TraceSection analysisId="an-1" status="Succeeded" />);

    await user.click(screen.getByRole('button', { name: 'Show trace' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Not found'));
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
