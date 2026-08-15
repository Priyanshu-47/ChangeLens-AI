import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useProjects } from '../projects/ProjectContext';
import { useAsync } from '../hooks/useAsync';
import { analysesApi, incidentsApi } from '../api/endpoints';
import { Timeline } from '../components/Timeline';
import {
  AnalysisStatusBadge,
  EmptyState,
  ErrorState,
  IncidentStatusBadge,
  SectionHeading,
  SeverityBadge,
  SkeletonRows,
} from '../components/ui';

export function IncidentDetailPage() {
  const { incidentId = '' } = useParams();
  const { selected } = useProjects();
  const navigate = useNavigate();

  const { data: incident, loading, error } = useAsync(() => incidentsApi.get(incidentId), [incidentId]);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const investigations = useAsync(
    async () => (selected?.id ? analysesApi.list(selected.id, { type: 'IncidentInvestigation', incidentId, pageSize: 10 }) : null),
    [selected?.id, incidentId, incident?.id],
  );

  const requestId = useMemo(() => crypto.randomUUID(), [incidentId]);

  const handleInvestigate = async () => {
    setSubmitError(null);
    setSubmitting(true);
    try {
      const accepted = await incidentsApi.investigate(incidentId, requestId);
      navigate(`/analyses/${accepted.analysisId}`);
    } catch (e) {
      setSubmitError(e instanceof Error ? e.message : 'Failed to start the investigation.');
      setSubmitting(false);
    }
  };

  if (loading) {
    return <SkeletonRows rows={6} />;
  }

  if (error || !incident) {
    return <ErrorState message={error ?? 'Incident not found.'} />;
  }

  return (
    <div>
      <div style={{ marginBottom: 6 }}>
        <Link to="/incidents" className="small">← Incidents</Link>
      </div>

      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 16, flexWrap: 'wrap' }}>
        <div style={{ flex: 1, minWidth: 260 }}>
          <h1 className="page-title">{incident.title}</h1>
          <p className="page-subtitle">
            Incident <span className="mono">{incident.id}</span> · created{' '}
            {new Date(incident.createdAtUtc).toLocaleString()}
          </p>
        </div>
        <button
          type="button"
          className="btn btn-primary"
          onClick={handleInvestigate}
          disabled={submitting || incident.status === 'Closed'}
          data-testid="investigate-button"
        >
          {submitting ? 'Submitting…' : 'Investigate Incident'}
        </button>
      </div>

      {submitError ? <ErrorState message={submitError} /> : null}

      <div className="meta-grid" style={{ marginTop: 10 }}>
        <div className="meta-item">
          <div className="meta-label">Severity</div>
          <div style={{ marginTop: 4 }}><SeverityBadge severity={incident.severity} /></div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Status</div>
          <div style={{ marginTop: 4 }}><IncidentStatusBadge status={incident.status} /></div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Environment</div>
          <div className="meta-value">{incident.environment ?? '—'}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Started</div>
          <div className="meta-value">{new Date(incident.startedAtUtc).toLocaleString()}</div>
        </div>
        {incident.detectedAtUtc ? (
          <div className="meta-item">
            <div className="meta-label">Detected</div>
            <div className="meta-value">{new Date(incident.detectedAtUtc).toLocaleString()}</div>
          </div>
        ) : null}
        {incident.affectedServiceId ? (
          <div className="meta-item">
            <div className="meta-label">Service</div>
            <div className="meta-value">{incident.affectedServiceId}</div>
          </div>
        ) : null}
      </div>

      <div className="grid-2">
        <section className="section">
          <SectionHeading title="Summary" />
          <div className="card card-body">
            {incident.summary ? <p style={{ margin: 0 }}>{incident.summary}</p> : <p className="muted" style={{ margin: 0 }}>No summary recorded.</p>}
          </div>

          <SectionHeading title="Known facts" />
          <div className="card card-body">
            <ul className="fact-list">
              <li><span className="fact-key">Severity</span><span>{incident.severity}</span></li>
              <li><span className="fact-key">Status</span><span>{incident.status}</span></li>
              {incident.classification ? <li><span className="fact-key">Classification</span><span>{incident.classification}</span></li> : null}
              {incident.environment ? <li><span className="fact-key">Environment</span><span>{incident.environment}</span></li> : null}
            </ul>
          </div>
        </section>

        <section className="section">
          <SectionHeading title="Timeline" />
          <div className="card card-body">
            <Timeline events={incident.events} />
          </div>
        </section>
      </div>

      <section className="section">
        <SectionHeading
          title="Investigations"
          right={investigations.loading ? <span className="spinner" style={{ width: 14, height: 14 }} /> : undefined}
        />
        <div className="card">
          {investigations.loading ? (
            <div style={{ padding: 16 }}><SkeletonRows rows={2} /></div>
          ) : investigations.error ? (
            <div style={{ padding: 16 }}><ErrorState message={investigations.error} /></div>
          ) : investigations.data && investigations.data.items.length > 0 ? (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Analysis</th>
                  <th>Status</th>
                  <th>Model</th>
                  <th>Completed</th>
                </tr>
              </thead>
              <tbody>
                {investigations.data.items.map((run) => (
                  <tr key={run.id}>
                    <td>
                      <Link className="row-link mono" to={`/analyses/${run.id}`}>
                        {run.id.slice(0, 8)}…
                      </Link>
                    </td>
                    <td><AnalysisStatusBadge status={run.status} /></td>
                    <td className="mono muted">{run.model ?? '—'}</td>
                    <td className="mono muted">{run.completedAtUtc ? new Date(run.completedAtUtc).toLocaleString() : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <div style={{ padding: 16 }}>
              <EmptyState
                icon="◫"
                title="No investigations yet"
                body="Run an investigation to get evidence-linked root-cause candidates."
                action={
                  <button type="button" className="btn btn-primary btn-sm" onClick={handleInvestigate} disabled={submitting}>
                    Investigate Incident
                  </button>
                }
              />
            </div>
          )}
        </div>
      </section>

    </div>
  );
}
