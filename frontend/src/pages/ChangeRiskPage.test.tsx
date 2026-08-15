import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ProjectProvider } from '../projects/ProjectContext';
import { ChangeRiskPage } from './ChangeRiskPage';
import { installFetchMock } from '../test/helpers';
import type { ChangeRiskResponse } from '../api/types';

const project = { id: 'p1', name: 'AcmePay', slug: 'acmepay', description: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, memberRole: 'Engineer' };

const riskResponse: ChangeRiskResponse = {
  analysisType: 'ChangeRisk',
  analysisRunId: null,
  usage: {
    model: 'mock',
    promptVersion: 'risk-v1',
    latencyMs: 12,
    inputTokens: 0,
    outputTokens: 0,
    totalTokens: 0,
    estimatedCostUsd: 0,
    validationStatus: 'valid',
    repairAttempts: 0,
    evidenceTruncated: false,
  },
  result: {
    riskLevel: 'MEDIUM',
    confidence: 0.72,
    impactedComponents: [
      { componentId: 'comp-1', name: 'TokenService', service: 'AuthService', filePath: 'src/AcmePay.Application/Auth/TokenService.cs', impact: 'direct' },
    ],
    riskFactors: [
      {
        id: 'f1',
        title: 'Key rotation invalidates issued tokens',
        description: 'Tokens issued before the rotation stop validating once the old key is removed.',
        severity: 'HIGH',
        evidence: [{ type: 'source', reference: 'E1' }],
        unknowns: [],
      },
    ],
    historicalIncidents: [],
    recommendedTests: [
      { category: 'integration', targetComponent: 'TokenService', description: 'Verify tokens issued before rotation still validate.' },
    ],
    unknowns: ['No load-test data was supplied.'],
    evidence: [
      { id: 'E1', type: 'Source Code', reference: 'src/AcmePay.Application/Auth/TokenService.cs', summary: 'IssueServiceToken validates against the active signing key.', aiDocumentId: null },
    ],
  },
};

function renderChangeRisk() {
  return render(
    <MemoryRouter initialEntries={['/change-risk']}>
      <ProjectProvider>
        <Routes>
          <Route path="/change-risk" element={<ChangeRiskPage />} />
        </Routes>
      </ProjectProvider>
    </MemoryRouter>,
  );
}

describe('ChangeRiskPage', () => {
  it('submits a change and renders the grounded risk report', async () => {
    const user = userEvent.setup();
    installFetchMock([
      { path: '/api/v1/projects', body: { items: [project], page: 1, pageSize: 50, total: 1 } },
      { method: 'POST', path: '/api/v1/analyses/change-risk', body: riskResponse },
    ]);
    renderChangeRisk();

    // Wait for the project to load so the form is enabled.
    await waitFor(() => expect(screen.getByLabelText('Change summary')).toBeEnabled());

    await user.type(screen.getByLabelText('Change summary'), 'Rotate the JWT signing key.');
    await user.type(screen.getByLabelText('File path 1'), 'src/AcmePay.Application/Auth/TokenService.cs');
    await user.click(screen.getByRole('button', { name: 'Run change-risk analysis' }));

    expect(await screen.findByText('RELEASE RISK')).toBeInTheDocument();
    expect(screen.getByText('MEDIUM')).toBeInTheDocument();
    expect(screen.getByText('TokenService')).toBeInTheDocument();
    expect(screen.getByText('Key rotation invalidates issued tokens')).toBeInTheDocument();
    // 'E1' appears both as a risk-factor reference and as an evidence card id.
    expect(screen.getAllByText('E1').length).toBeGreaterThan(0);
    expect(screen.getByText('No load-test data was supplied.')).toBeInTheDocument();
    expect(screen.getByText(/Verify tokens issued before rotation still validate\./)).toBeInTheDocument();
  });

  it('shows the not-selected guard when no project is available', async () => {
    installFetchMock([{ path: '/api/v1/projects', body: { items: [], page: 1, pageSize: 50, total: 0 } }]);
    renderChangeRisk();
    expect(await screen.findByText('Select a project before running a change-risk analysis.')).toBeInTheDocument();
  });
});
