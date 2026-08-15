import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useProjects } from '../projects/ProjectContext';
import { useAsync } from '../hooks/useAsync';
import { analysesApi } from '../api/endpoints';
import { AnalysisStatusBadge, EmptyState, ErrorState, SkeletonRows } from '../components/ui';

export function AnalysesPage() {
  const { selected } = useProjects();
  const projectId = selected?.id;
  const [statusFilter, setStatusFilter] = useState('');

  const { data, loading, error, run } = useAsync(
    async () => (projectId ? analysesApi.list(projectId, { status: statusFilter || undefined, pageSize: 50 }) : null),
    [projectId, statusFilter],
  );

  return (
    <div>
      <h1 className="page-title">Analyses</h1>
      <p className="page-subtitle">Async analysis runs in {selected?.name ?? 'the selected project'} — poll each one for its result.</p>

      <div className="field" style={{ maxWidth: 220, marginBottom: 16 }}>
        <label htmlFor="analysis-status-filter">Status</label>
        <select id="analysis-status-filter" className="select" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
          <option value="">All</option>
          <option>Queued</option>
          <option>Running</option>
          <option>Succeeded</option>
          <option>Failed</option>
        </select>
      </div>

      <div className="card">
        {loading ? (
          <div style={{ padding: 16 }}><SkeletonRows rows={5} /></div>
        ) : error ? (
          <div style={{ padding: 16 }}><ErrorState message={error} onRetry={() => void run()} /></div>
        ) : data && data.items.length > 0 ? (
          <table className="data-table">
            <thead>
              <tr>
                <th>Analysis</th>
                <th>Type</th>
                <th>Status</th>
                <th>Model</th>
                <th>Prompt</th>
                <th>Completed</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((runItem) => (
                <tr key={runItem.id}>
                  <td>
                    <Link className="row-link mono" to={`/analyses/${runItem.id}`}>
                      {runItem.id.slice(0, 8)}…
                    </Link>
                  </td>
                  <td>{runItem.type}</td>
                  <td><AnalysisStatusBadge status={runItem.status} /></td>
                  <td className="mono muted">{runItem.model ?? '—'}</td>
                  <td className="mono muted">{runItem.promptVersion ?? '—'}</td>
                  <td className="mono muted">
                    {runItem.completedAtUtc ? new Date(runItem.completedAtUtc).toLocaleString() : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <div style={{ padding: 16 }}>
            <EmptyState
              icon="◫"
              title="No analyses yet"
              body="Run an incident investigation or a change-risk analysis to see runs here."
              action={
                <Link className="btn btn-primary btn-sm" to="/incidents">
                  Open incidents
                </Link>
              }
            />
          </div>
        )}
      </div>
    </div>
  );
}
