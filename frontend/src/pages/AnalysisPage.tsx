import { Link, useParams } from 'react-router-dom';
import { useAnalysisPolling } from '../hooks/useAnalysisPolling';
import type { IncidentInvestigationResult } from '../api/types';
import {
  AnalysisStatusBadge,
  EmptyState,
  ErrorState,
  GroundingBadge,
  SectionHeading,
  SkeletonRows,
} from '../components/ui';
import {
  buildEvidenceMap,
  CandidateCard,
  DistinctionBanner,
  EvidencePanel,
  RemediationSection,
  UnknownsBlock,
} from '../components/Investigation';
import { TraceSection } from '../components/Trace';

function isIncidentResult(result: unknown): result is IncidentInvestigationResult {
  return (
    typeof result === 'object' &&
    result !== null &&
    Array.isArray((result as IncidentInvestigationResult).rootCauseCandidates) &&
    typeof (result as IncidentInvestigationResult).remediation === 'object' &&
    (result as IncidentInvestigationResult).remediation !== null
  );
}

export function AnalysisPage() {
  const { analysisId = '' } = useParams();
  const { run, loading, error, active, settled } = useAnalysisPolling(analysisId);

  if (loading && !run) {
    return <SkeletonRows rows={6} />;
  }

  if (!run) {
    if (error) {
      return <ErrorState message={error} />;
    }
    return <EmptyState icon="◫" title="Analysis not found" />;
  }

  return (
    <div>
      <div style={{ marginBottom: 6 }}>
        <Link to="/analyses" className="small">← Analyses</Link>
      </div>

      {/* Status hero */}
      <div className="card analysis-hero">
        <div className="analysis-status-icon" aria-hidden="true">
          {run.status === 'Succeeded' ? '✓' : run.status === 'Failed' ? '✕' : '◌'}
        </div>
        <div style={{ flex: 1 }}>
          <h1 className="page-title" style={{ fontSize: 17 }}>
            {run.type === 'ChangeRisk' ? 'Change-risk analysis' : 'Incident investigation'}
          </h1>
          <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
            <AnalysisStatusBadge status={run.status} />
            {run.status === 'Running' ? (
              <span className="small muted">
                Analyzing {run.type === 'ChangeRisk' ? 'change risk' : 'incident'}…
              </span>
            ) : null}
            {run.status === 'Succeeded' && isIncidentResult(run.result) ? (
              <GroundingBadge validationStatus="valid" />
            ) : null}
          </div>
        </div>
        <div className="mono faint">{run.id.slice(0, 8)}…</div>
      </div>

      {/* Metadata */}
      <div className="meta-grid" style={{ marginTop: 16 }}>
        <div className="meta-item">
          <div className="meta-label">Analysis ID</div>
          <div className="meta-value">{run.id}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Type</div>
          <div className="meta-value">{run.type}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Model</div>
          <div className="meta-value">{run.model ?? '—'}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Prompt version</div>
          <div className="meta-value">{run.promptVersion ?? '—'}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Result schema</div>
          <div className="meta-value">{run.resultSchemaVersion ?? '—'}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Queued</div>
          <div className="meta-value">{run.queuedAtUtc ? new Date(run.queuedAtUtc).toLocaleString() : '—'}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Started</div>
          <div className="meta-value">{run.startedAtUtc ? new Date(run.startedAtUtc).toLocaleString() : '—'}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Completed</div>
          <div className="meta-value">{run.completedAtUtc ? new Date(run.completedAtUtc).toLocaleString() : '—'}</div>
        </div>
      </div>

      {error ? <ErrorState message={error} /> : null}

      {/* Failed state */}
      {run.status === 'Failed' ? (
        <section className="section">
          <SectionHeading title="Analysis failed" />
          <div className="error-state" role="alert">
            <div>
              <strong>{run.error?.code ?? 'UNKNOWN'}</strong> — {run.error?.message ?? 'The analysis failed.'}
            </div>
            <div className="error-trace">The safe failure message above is what the backend exposes; no stack traces or secrets are shown.</div>
          </div>
        </section>
      ) : null}

      {/* Running / queued state */}
      {active && run.status !== 'Succeeded' ? (
        <section className="section">
          <div className="card card-body">
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <span className="spinner" />
              <span>
                <strong>Analysis in progress.</strong>{' '}
                <span className="muted small">
                  The backend reports {run.status} — the worker is assembling incident context,
                  retrieving evidence, and running the grounded AI investigation. Polling every 2.5s.
                </span>
              </span>
            </div>
          </div>
        </section>
      ) : null}

      {/* Succeeded: incident investigation result */}
      {run.status === 'Succeeded' && isIncidentResult(run.result) ? (
        <InvestigationResultBody result={run.result} />
      ) : null}

      {/* Succeeded: change-risk result */}
      {run.status === 'Succeeded' && !isIncidentResult(run.result) && run.result !== null ? (
        <p className="muted small">
          This analysis ran the change-risk workflow. View its result on the Change Risk page
          (re-run) — or see the raw result below.
        </p>
      ) : null}

      {/* Phase 7 observability trace (stages + retrieval explorer) */}
      <TraceSection analysisId={run.id} status={run.status} />

      {/* Polling indicator when stuck-but-settled without a terminal run */}
      {settled && !run ? <EmptyState icon="◫" title="Analysis not found" body="It may have been removed or you may not have access." /> : null}
    </div>
  );
}

function InvestigationResultBody({ result }: { result: IncidentInvestigationResult }) {
  const evidenceById = buildEvidenceMap(result);

  return (
    <>
      <section className="section" style={{ marginTop: 22 }}>
        <DistinctionBanner />
        <SectionHeading
          title="Root cause candidates"
          right={<span className="small muted">{result.rootCauseCandidates.length} candidates</span>}
        />
        {result.rootCauseCandidates.length === 0 ? (
          <div className="card card-body">
            <EmptyState
              icon="◍"
              title="No root-cause candidates"
              body="The evidence did not support a specific hypothesis — see what was unknown below."
            />
          </div>
        ) : (
          result.rootCauseCandidates.map((candidate, index) => (
            <CandidateCard key={candidate.id ?? index} candidate={candidate} index={index} evidenceById={evidenceById} />
          ))
        )}
        <p className="confidence-note" style={{ marginTop: 8 }}>
          Model confidence reflects the AI's assessment based on the supplied evidence — it is not proof.
        </p>
      </section>

      <section className="section">
        <SectionHeading title="Evidence" right={<span className="small muted">{result.evidence.length} items</span>} />
        <div className="card card-body">
          <EvidencePanel evidence={result.evidence} />
        </div>
      </section>

      <section className="section">
        <SectionHeading title="Remediation" />
        <RemediationSection remediation={result.remediation} />
      </section>

      <section className="section">
        <SectionHeading title="Unknown / missing information" />
        <UnknownsBlock unknowns={result.unknowns} />
      </section>
    </>
  );
}
