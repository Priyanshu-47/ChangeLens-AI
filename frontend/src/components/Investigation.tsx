import { useState } from 'react';
import type { IncidentEvidence, IncidentInvestigationResult, Remediation, RootCauseCandidate } from '../api/types';

// ── EVIDENCE vs ANALYSIS distinction (core product principle, brief §22) ─

export function DistinctionBanner() {
  return (
    <div className="distinction" role="note">
      <span aria-hidden="true">⟐</span>
      <span>
        <strong>Evidence</strong> is what the system retrieved — inspectable, traceable artifacts.
        {' '}
        <strong>Analysis</strong> is what the AI inferred from that evidence. Every root-cause
        candidate links to the evidence ids it relies on.
      </span>
    </div>
  );
}

// ── Root-cause candidate ────────────────────────────────────────────────

export function CandidateCard({
  candidate,
  index,
  evidenceById,
}: {
  candidate: RootCauseCandidate;
  index: number;
  evidenceById: Map<string, IncidentEvidence>;
}) {
  const [open, setOpen] = useState(index === 0);
  const confidencePct = Math.round(candidate.confidence * 100);

  const linkToEvidence = (evidenceId: string) => {
    const el = document.getElementById(`evidence-${cssEscape(evidenceId)}`);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      el.focus();
    }
  };

  return (
    <article className="candidate">
      <button
        type="button"
        className="candidate-header"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-controls={`candidate-${index}`}
      >
        <span className="candidate-rank" aria-hidden="true">{index + 1}</span>
        <span className="candidate-title">{candidate.title}</span>
        <span className="candidate-meta">
          <span>Confidence {confidencePct}%</span>
          <span>{candidate.evidenceIds.length} evidence</span>
          <span aria-hidden="true">{open ? '▾' : '▸'}</span>
        </span>
      </button>

      {open ? (
        <div className="candidate-body" id={`candidate-${index}`}>
          <div className="confidence-row" role="img" aria-label={`Confidence ${confidencePct} percent`}>
            <span className="confidence-bar">
              <span className="confidence-fill" style={{ width: `${confidencePct}%` }} />
            </span>
            <span className="confidence-label">{confidencePct}%</span>
          </div>

          {candidate.reasoning ? <p className="small" style={{ color: 'var(--text-secondary)' }}>{candidate.reasoning}</p> : null}

          <div className="small" style={{ marginBottom: 4, color: 'var(--text-muted)' }}>
            Grounded in:
          </div>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {candidate.evidenceIds.map((evidenceId) => {
              const ev = evidenceById.get(evidenceId);
              return (
                <button
                  key={evidenceId}
                  type="button"
                  className="link-chip"
                  onClick={() => linkToEvidence(evidenceId)}
                  title={`Scroll to ${evidenceId}${ev?.type ? ` (${ev.type})` : ''}`}
                >
                  <span aria-hidden="true">↓</span> {evidenceId}
                </button>
              );
            })}
          </div>

          {candidate.unknowns.length > 0 ? (
            <div className="small" style={{ marginTop: 10, color: 'var(--warning)' }}>
              Candidate unknowns: {candidate.unknowns.join(' · ')}
            </div>
          ) : null}
        </div>
      ) : null}
    </article>
  );
}

// ── Evidence panel ──────────────────────────────────────────────────────

export function EvidencePanel({ evidence }: { evidence: IncidentEvidence[] }) {
  if (evidence.length === 0) {
    return <p className="muted small">No evidence was retrieved for this investigation.</p>;
  }

  return (
    <div className="evidence-list">
      {evidence.map((item) => (
        <div
          key={item.id}
          id={`evidence-${cssEscape(item.id)}`}
          className="evidence-card"
          tabIndex={0}
          aria-label={`Evidence ${item.id}`}
        >
          <div className="evidence-head">
            <span className="badge badge-accent">{item.type}</span>
            <span className="evidence-id">{item.id}</span>
          </div>
          {item.summary ? <p className="evidence-summary">{item.summary}</p> : null}
          {item.source ? <div className="evidence-meta">source: {item.source}</div> : null}
          {Object.keys(item.metadata ?? {}).length > 0 ? (
            <div className="evidence-meta">
              {Object.entries(item.metadata)
                .filter(([, v]) => v !== null && v !== undefined)
                .map(([k, v]) => (
                  <span key={k} style={{ marginRight: 10 }}>
                    {k}: {String(v)}
                  </span>
                ))}
            </div>
          ) : null}
        </div>
      ))}
    </div>
  );
}

// ── Remediation ─────────────────────────────────────────────────────────

function RemediationRow({ label, value }: { label: string; value: string | string[] | null }) {
  if (!value || (Array.isArray(value) && value.length === 0)) {
    return null;
  }
  return (
    <div className="fact-list" style={{ marginBottom: 12 }}>
      <li>
        <span className="fact-key">{label}</span>
        {Array.isArray(value) ? (
          <span>
            <ul className="checklist" style={{ padding: 0 }}>
              {value.map((item, i) => (
                <li key={i}>{item}</li>
              ))}
            </ul>
          </span>
        ) : (
          <span>{value}</span>
        )}
      </li>
    </div>
  );
}

export function RemediationSection({ remediation }: { remediation: Remediation }) {
  return (
    <div className="card">
      <div className="card-header">
        <h3 className="card-title">Remediation</h3>
        {remediation.insufficientEvidence ? (
          <span className="badge badge-warning">Insufficient evidence</span>
        ) : null}
      </div>
      <div className="card-body">
        {remediation.insufficientEvidence ? (
          <p className="small" style={{ color: 'var(--warning)' }}>
            The available evidence was insufficient to give operational remediation — the AI
            flagged this rather than inventing procedures.
          </p>
        ) : null}
        <RemediationRow label="Immediate mitigation" value={remediation.immediateMitigation} />
        <RemediationRow label="Investigation steps" value={remediation.investigationSteps} />
        <RemediationRow label="Recommended remediation" value={remediation.recommendedRemediation} />
        <RemediationRow label="Validation steps" value={remediation.validationSteps} />
        <RemediationRow label="Rollback consideration" value={remediation.rollbackConsideration} />
      </div>
    </div>
  );
}

// ── Unknowns (explicitly separated from root causes, brief §20) ─────────

export function UnknownsBlock({ unknowns }: { unknowns: string[] }) {
  if (unknowns.length === 0) {
    return null;
  }
  return (
    <div className="unknown-block">
      <h3>Unknown / missing information</h3>
      <ul className="unknown-list">
        {unknowns.map((u, i) => (
          <li key={i}>{u}</li>
        ))}
      </ul>
    </div>
  );
}

export function buildEvidenceMap(result: IncidentInvestigationResult): Map<string, IncidentEvidence> {
  const map = new Map<string, IncidentEvidence>();
  for (const item of result.evidence) {
    map.set(item.id, item);
  }
  return map;
}

// CSS.escape may be unavailable in very old runtimes; ids are backend-generated
// UUIDs/chunk ids, so this is a safety net.
function cssEscape(value: string): string {
  if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
    return CSS.escape(value);
  }
  return value.replace(/[^a-zA-Z0-9_-]/g, '_');
}
