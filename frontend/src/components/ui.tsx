import type { ReactNode } from 'react';
import type { AnalysisStatus, IncidentSeverity, RiskLevel } from '../api/types';

// ── Status badges (text + color, never color alone — brief §31) ────────

export function AnalysisStatusBadge({ status }: { status: AnalysisStatus }) {
  const tone =
    status === 'Succeeded' ? 'success' : status === 'Failed' ? 'danger' : status === 'Running' ? 'info' : 'muted';
  return (
    <span className={`badge badge-${tone}`} role="status">
      <span className="dot" aria-hidden="true" />
      {status}
    </span>
  );
}

export function SeverityBadge({ severity }: { severity: IncidentSeverity }) {
  const cls = `severity-${severity.toLowerCase()}`;
  return (
    <span className={`badge ${cls}`}>
      {severity}
    </span>
  );
}

export function IncidentStatusBadge({ status }: { status: string }) {
  const tone =
    status === 'Open' ? 'danger' : status === 'Investigating' ? 'warning' : status === 'Resolved' ? 'success' : 'muted';
  return (
    <span className={`badge badge-${tone}`}>
      {status}
    </span>
  );
}

export function RiskBadge({ level }: { level: RiskLevel }) {
  const cls = `risk-${level.toLowerCase()}`;
  return (
    <span className={`badge ${cls}`}>
      {level}
    </span>
  );
}

export function GroundingBadge({ validationStatus }: { validationStatus: string | null | undefined }) {
  const valid = validationStatus === 'valid';
  return (
    <span className={`badge ${valid ? 'badge-success' : 'badge-danger'}`} title="Grounding reflects the backend's deterministic evidence validation — the UI does not recalculate it.">
      <span className="dot" aria-hidden="true" />
      Grounding: {valid ? 'VALID' : 'INVALID'}
    </span>
  );
}

// ── Loading ─────────────────────────────────────────────────────────────

export function Spinner({ label }: { label?: string }) {
  return (
    <div className="page-center">
      <span className="spinner" role="status" aria-label={label ?? 'Loading'} />
      {label ? <span style={{ marginLeft: 10 }}>{label}</span> : null}
    </div>
  );
}

export function SkeletonRows({ rows = 4 }: { rows?: number }) {
  return (
    <div aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="skeleton" style={{ width: `${90 - i * 12}%` }} />
      ))}
    </div>
  );
}

// ── Empty / error ───────────────────────────────────────────────────────

export function EmptyState({ icon = '∅', title, body, action }: { icon?: string; title: string; body?: string; action?: ReactNode }) {
  return (
    <div className="empty-state">
      <div className="empty-icon" aria-hidden="true">{icon}</div>
      <h3>{title}</h3>
      {body ? <p>{body}</p> : null}
      {action}
    </div>
  );
}

export function ErrorState({ message, traceId, onRetry }: { message: string; traceId?: string | null; onRetry?: () => void }) {
  return (
    <div className="error-state" role="alert">
      <div>{message}</div>
      {traceId ? <div className="error-trace">Trace ID: {traceId}</div> : null}
      {onRetry ? (
        <button type="button" className="btn btn-sm" style={{ marginTop: 8 }} onClick={onRetry}>
          Retry
        </button>
      ) : null}
    </div>
  );
}

export function SectionHeading({ title, right }: { title: string; right?: ReactNode }) {
  return (
    <div className="section-heading">
      <h2>{title}</h2>
      {right}
    </div>
  );
}

export function Kicker({ children }: { children: ReactNode }) {
  return <p className="section-kicker">{children}</p>;
}
