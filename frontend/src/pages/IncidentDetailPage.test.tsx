import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom';
import { ProjectProvider } from '../projects/ProjectContext';
import { IncidentDetailPage } from './IncidentDetailPage';
import { installFetchMock } from '../test/helpers';
import type { Incident } from '../api/types';

const incident: Incident = {
  id: 'inc-1',
  projectId: 'p1',
  title: 'HTTP 401 after JWT signing-key rotation',
  severity: 'Sev2',
  status: 'Investigating',
  classification: 'auth',
  affectedServiceId: null,
  environment: 'production',
  startedAtUtc: '2026-08-01T10:30:00Z',
  detectedAtUtc: '2026-08-01T10:31:00Z',
  summary: 'Authentication requests started failing after the signing-key rotation.',
  createdAtUtc: '2026-08-01T10:32:00Z',
  events: [
    { id: 'e1', occurredAtUtc: '2026-08-01T10:30:00Z', type: 'Deployment', source: 'deploybot', message: 'JWT signing key rotated', rawData: null },
    { id: 'e2', occurredAtUtc: '2026-08-01T10:31:00Z', type: 'Error', source: 'auth-api', message: 'HTTP 401 spike', rawData: null },
  ],
};

function AnalysisStub() {
  const { analysisId } = useParams();
  return <div>Analysis stub for {analysisId}</div>;
}

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={['/incidents/inc-1']}>
      <ProjectProvider>
        <Routes>
          <Route path="/incidents/:incidentId" element={<IncidentDetailPage />} />
          <Route path="/analyses/:analysisId" element={<AnalysisStub />} />
        </Routes>
      </ProjectProvider>
    </MemoryRouter>,
  );
}

describe('IncidentDetailPage', () => {
  it('renders incident metadata and the chronological timeline from actual data', async () => {
    installFetchMock([
      { path: '/api/v1/projects', body: { items: [{ id: 'p1', name: 'AcmePay', slug: 'acmepay', description: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, memberRole: 'Engineer' }], page: 1, pageSize: 50, total: 1 } },
      { path: '/api/v1/incidents/inc-1', body: incident },
      { path: '/api/v1/analyses', body: { items: [], page: 1, pageSize: 10, total: 0 } },
    ]);
    renderDetail();

    // All async renders go through mocked fetches; under parallel test workers the
    // default 1s findBy timeout occasionally loses the race, so every wait uses a
    // generous timeout (assertion-hardening, not a product change).
    expect(await screen.findByText('HTTP 401 after JWT signing-key rotation', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(await screen.findByText('JWT signing key rotated', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(await screen.findByText('HTTP 401 spike', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(
      await screen.findByText('No investigations yet', undefined, { timeout: 5000 }),
    ).toBeInTheDocument();

    // Timeline is chronological: deployment event precedes the error event.
    const items = screen.getAllByRole('listitem').filter((el) => el.className.includes('timeline-item'));
    const deployment = items.find((el) => el.textContent?.includes('JWT signing key rotated'));
    const error = items.find((el) => el.textContent?.includes('HTTP 401 spike'));
    expect(deployment && error).toBeTruthy();
    expect(deployment!.compareDocumentPosition(error!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('submits the investigation, handles 202, and navigates to the analysis page', async () => {
    const user = userEvent.setup();
    installFetchMock([
      { path: '/api/v1/projects', body: { items: [{ id: 'p1', name: 'AcmePay', slug: 'acmepay', description: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, memberRole: 'Engineer' }], page: 1, pageSize: 50, total: 1 } },
      { path: '/api/v1/incidents/inc-1', body: incident },
      { path: '/api/v1/analyses', body: { items: [], page: 1, pageSize: 10, total: 0 } },
      {
        method: 'POST',
        path: '/api/v1/incidents/inc-1/investigate',
        status: 202,
        body: { analysisId: 'an-1', status: 'Queued', statusUrl: '/api/v1/analyses/an-1' },
      },
    ]);
    renderDetail();

    const button = await screen.findByTestId('investigate-button');
    await user.click(button);

    await waitFor(() => expect(screen.getByText('Analysis stub for an-1')).toBeInTheDocument());
  });

  it('surfaces a friendly error when the investigation submission fails', async () => {
    const user = userEvent.setup();
    installFetchMock([
      { path: '/api/v1/projects', body: { items: [{ id: 'p1', name: 'AcmePay', slug: 'acmepay', description: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, memberRole: 'Engineer' }], page: 1, pageSize: 50, total: 1 } },
      { path: '/api/v1/incidents/inc-1', body: incident },
      { path: '/api/v1/analyses', body: { items: [], page: 1, pageSize: 10, total: 0 } },
      {
        method: 'POST',
        path: '/api/v1/incidents/inc-1/investigate',
        status: 403,
        body: { type: 'problem', title: 'Forbidden', status: 403, code: 'forbidden' },
      },
    ]);
    renderDetail();

    const button = await screen.findByTestId('investigate-button');
    await user.click(button);

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Forbidden'));
    expect(screen.queryByText('Analysis stub for an-1')).not.toBeInTheDocument();
  });
});
