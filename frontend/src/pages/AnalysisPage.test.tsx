import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AnalysisPage } from './AnalysisPage';
import { installFetchMock } from '../test/helpers';
import type { AnalysisRun } from '../api/types';

const succeededRun: AnalysisRun = {
  id: 'an-1',
  projectId: 'p1',
  type: 'IncidentInvestigation',
  status: 'Succeeded',
  incidentId: 'inc-1',
  result: {
    rootCauseCandidates: [
      {
        id: 'c1',
        title: 'JWT signing key mismatch',
        confidence: 0.81,
        status: 'likely',
        evidenceIds: ['E1', 'E4'],
        reasoning: 'The validator and issuer disagree on the active signing key after the rotation.',
        unknowns: ['Exact key version at failure time'],
      },
      {
        id: 'c2',
        title: 'Cached public keys in the gateway',
        confidence: 0.55,
        status: 'possible',
        evidenceIds: ['E7'],
        reasoning: 'Gateway caches may still hold the pre-rotation key.',
        unknowns: [],
      },
    ],
    remediation: {
      immediateMitigation: 'Roll back to the previous signing key.',
      investigationSteps: ['Confirm which key version the auth API validates with.'],
      recommendedRemediation: null,
      validationSteps: [],
      rollbackConsideration: null,
      insufficientEvidence: false,
    },
    unknowns: ['No database telemetry was available.', 'No application log sample was supplied.'],
    evidence: [
      { id: 'E1', type: 'Source Code', source: 'TokenService.cs', summary: 'IssueServiceToken validates the JWT against the current signing key.', metadata: { language: 'csharp' } },
      { id: 'E4', type: 'Runbook', source: 'auth-001-jwt-key-rotation', summary: 'Signing-key rotation requires updating issuer and validator together.', metadata: {} },
      { id: 'E7', type: 'Historical Incident', source: 'inc-9', summary: 'Prior 401 spike after a gateway cache remained warm.', metadata: {} },
    ],
  },
  resultSchemaVersion: 'incident-v1',
  model: 'mock',
  promptVersion: 'incident-v1',
  queuedAtUtc: '2026-08-01T10:00:00Z',
  startedAtUtc: '2026-08-01T10:00:01Z',
  completedAtUtc: '2026-08-01T10:00:30Z',
  error: null,
};

function renderAnalysis(run: AnalysisRun) {
  installFetchMock([{ method: 'GET', path: /\/api\/v1\/analyses\/an-1$/, body: run }]);
  return render(
    <MemoryRouter initialEntries={['/analyses/an-1']}>
      <Routes>
        <Route path="/analyses/:analysisId" element={<AnalysisPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AnalysisPage', () => {
  it('renders the succeeded investigation result: candidates, evidence, remediation, unknowns', async () => {
    renderAnalysis(succeededRun);

    expect(await screen.findByText('Incident investigation')).toBeInTheDocument();
    expect(screen.getByText('Root cause candidates')).toBeInTheDocument();
    expect(screen.getByText('JWT signing key mismatch')).toBeInTheDocument();
    expect(screen.getByText('Confidence 81%')).toBeInTheDocument();
    // Grounding indicator (backend-computed, never recalculated in the UI).
    expect(screen.getByText(/Grounding: VALID/)).toBeInTheDocument();
    // Evidence vs analysis distinction (text spans multiple nodes, match a fragment).
    expect(screen.getByText(/inspectable, traceable artifacts/)).toBeInTheDocument();
    // Evidence panel (sources are rendered with a 'source:' prefix).
    expect(screen.getByText('source: TokenService.cs')).toBeInTheDocument();
    expect(screen.getByText('source: auth-001-jwt-key-rotation')).toBeInTheDocument();
    // Remediation.
    expect(screen.getByText('Immediate mitigation')).toBeInTheDocument();
    expect(screen.getByText('Roll back to the previous signing key.')).toBeInTheDocument();
    // Unknowns — separate section, not mixed with root causes (heading appears
    // twice: the section heading and the UnknownsBlock heading).
    expect(screen.getAllByText('Unknown / missing information').length).toBeGreaterThan(0);
    expect(screen.getByText('No database telemetry was available.')).toBeInTheDocument();
    // Confidence disclaimer.
    expect(screen.getByText(/Model confidence reflects the AI's assessment/)).toBeInTheDocument();
  });

  it('expands a collapsed root-cause candidate and links its evidence ids to the evidence panel', async () => {
    const scrollSpy = vi.fn();
    Element.prototype.scrollIntoView = scrollSpy;
    const user = userEvent.setup();
    renderAnalysis(succeededRun);

    // Second candidate starts collapsed.
    const second = await screen.findByRole('button', { name: /Cached public keys in the gateway/ });
    expect(screen.queryByRole('button', { name: 'E7' })).not.toBeInTheDocument();

    await user.click(second);
    const chip = screen.getByRole('button', { name: 'E7' });
    expect(chip).toBeInTheDocument();

    // Evidence traceability: clicking the chip scrolls/focuses the matching evidence card.
    const evidenceCard = document.getElementById('evidence-E7');
    expect(evidenceCard).toBeInTheDocument();
    await user.click(chip);
    expect(scrollSpy).toHaveBeenCalled();
    expect(document.activeElement).toBe(evidenceCard);
  });

  it('renders the failed state with the safe failure code and message only', async () => {
    renderAnalysis({
      ...succeededRun,
      status: 'Failed',
      result: null,
      error: { code: 'AI_VALIDATION_FAILED', message: 'The AI returned a response that failed grounding validation.' },
    });

    expect(await screen.findByText('Analysis failed')).toBeInTheDocument();
    expect(screen.getByText(/AI_VALIDATION_FAILED/)).toBeInTheDocument();
    expect(screen.getByText(/The AI returned a response that failed grounding validation\./)).toBeInTheDocument();
    expect(screen.getByText(/no stack traces or secrets are shown/)).toBeInTheDocument();
    expect(screen.queryByText('Root cause candidates')).not.toBeInTheDocument();
  });

  it('renders the running state without fabricating internal stages', async () => {
    renderAnalysis({ ...succeededRun, status: 'Running', result: null });

    expect(await screen.findByText('Analyzing incident…')).toBeInTheDocument();
    expect(screen.getByText(/Analysis in progress/)).toBeInTheDocument();
    expect(screen.getByText(/The backend reports Running/)).toBeInTheDocument();
    expect(screen.queryByText('Root cause candidates')).not.toBeInTheDocument();
  });
});

describe('AnalysisPage empty state', () => {
  it('shows a not-found state when the analysis cannot be loaded', async () => {
    installFetchMock([
      { method: 'GET', path: /\/api\/v1\/analyses\/missing$/, status: 404, body: { type: 'problem', title: 'Not found', status: 404, code: 'not_found' } },
    ]);
    render(
      <MemoryRouter initialEntries={['/analyses/missing']}>
        <Routes>
          <Route path="/analyses/:analysisId" element={<AnalysisPage />} />
        </Routes>
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Not found'));
  });
});
