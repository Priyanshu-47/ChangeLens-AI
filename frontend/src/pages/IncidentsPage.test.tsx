import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ProjectProvider } from '../projects/ProjectContext';
import { IncidentsPage } from './IncidentsPage';
import { installFetchMock } from '../test/helpers';
import type { Incident } from '../api/types';

const project = { id: 'p1', name: 'AcmePay', slug: 'acmepay', description: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, memberRole: 'Engineer' };

const incidentA: Incident = {
  id: 'inc-1',
  projectId: 'p1',
  title: 'HTTP 401 after JWT signing-key rotation',
  severity: 'Sev2',
  status: 'Investigating',
  classification: 'auth',
  affectedServiceId: null,
  environment: 'production',
  startedAtUtc: '2026-08-01T10:30:00Z',
  detectedAtUtc: null,
  summary: 'Authentication requests started failing.',
  createdAtUtc: '2026-08-01T10:32:00Z',
  events: [],
};

const incidentB: Incident = {
  id: 'inc-2',
  projectId: 'p1',
  title: 'Checkout latency spike',
  severity: 'Sev3',
  status: 'Resolved',
  classification: 'performance',
  affectedServiceId: null,
  environment: 'production',
  startedAtUtc: '2026-07-20T09:00:00Z',
  detectedAtUtc: null,
  summary: 'Payment gateway latency increased.',
  createdAtUtc: '2026-07-20T09:05:00Z',
  events: [],
};

function renderIncidents() {
  return render(
    <MemoryRouter initialEntries={['/incidents']}>
      <ProjectProvider>
        <Routes>
          <Route path="/incidents" element={<IncidentsPage />} />
        </Routes>
      </ProjectProvider>
    </MemoryRouter>,
  );
}

describe('IncidentsPage', () => {
  it('renders the incident list rows from actual API data', async () => {
    installFetchMock([
      { path: '/api/v1/projects', body: { items: [project], page: 1, pageSize: 50, total: 1 } },
      { path: '/api/v1/incidents', body: { items: [incidentA, incidentB], page: 1, pageSize: 100, total: 2 } },
    ]);
    renderIncidents();

    expect(await screen.findByText('HTTP 401 after JWT signing-key rotation')).toBeInTheDocument();
    expect(screen.getByText('Checkout latency spike')).toBeInTheDocument();
    // 'Sev2' also exists as a filter <option>, so scope to the table.
    expect(screen.getAllByText('Sev2').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Resolved').length).toBeGreaterThan(0);
  });

  it('filters the rows with the local search box', async () => {
    const user = userEvent.setup();
    installFetchMock([
      { path: '/api/v1/projects', body: { items: [project], page: 1, pageSize: 50, total: 1 } },
      { path: '/api/v1/incidents', body: { items: [incidentA, incidentB], page: 1, pageSize: 100, total: 2 } },
    ]);
    renderIncidents();

    await screen.findByText('Checkout latency spike');
    await user.type(screen.getByLabelText('Search'), 'checkout');

    expect(screen.getByText('Checkout latency spike')).toBeInTheDocument();
    expect(screen.queryByText('HTTP 401 after JWT signing-key rotation')).not.toBeInTheDocument();
  });

  it('shows the empty state when there are no incidents', async () => {
    installFetchMock([
      { path: '/api/v1/projects', body: { items: [project], page: 1, pageSize: 50, total: 1 } },
      { path: '/api/v1/incidents', body: { items: [], page: 1, pageSize: 100, total: 0 } },
    ]);
    renderIncidents();

    expect(await screen.findByText('No incidents yet')).toBeInTheDocument();
  });
});
