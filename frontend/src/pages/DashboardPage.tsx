import { Link } from 'react-router-dom';
import { useProjects } from '../projects/ProjectContext';
import { useAsync } from '../hooks/useAsync';
import { analysesApi, codeModelApi, incidentsApi } from '../api/endpoints';
import { AnalysisStatusBadge, EmptyState, ErrorState, IncidentStatusBadge, SeverityBadge, SkeletonRows } from '../components/ui';

export function DashboardPage() {
  const { selected, projects, loading: projectsLoading, selectProject } = useProjects();
  const projectId = selected?.id;

  const incidents = useAsync(async () => (projectId ? incidentsApi.list(projectId, { pageSize: 5 }) : null), [projectId]);
  const analyses = useAsync(async () => (projectId ? analysesApi.list(projectId, { pageSize: 5 }) : null), [projectId]);
  const services = useAsync(async () => (projectId ? codeModelApi.services(projectId) : null), [projectId]);
  const repositories = useAsync(async () => (projectId ? codeModelApi.repositories(projectId) : null), [projectId]);

  if (projectsLoading) {
    return <SkeletonRows rows={3} />;
  }

  if (!selected) {
    return (
      <EmptyState
        icon="▦"
        title="No project selected"
        body="You are not a member of any project, or none are visible. Ask a project owner to add you."
      />
    );
  }

  const counts = {
    incidents: incidents.data?.total ?? 0,
    analyses: analyses.data?.total ?? 0,
    services: services.data?.total ?? 0,
    repositories: repositories.data?.total ?? 0,
  };

  return (
    <div>
      <h1 className="page-title">{selected.name}</h1>
      <p className="page-subtitle">
        Project <span className="mono">{selected.slug}</span> · your role: {selected.memberRole}
      </p>

      {/* Project switcher — also available in the sidebar */}
      {projects.length > 1 ? (
        <div className="field" style={{ maxWidth: 320 }}>
          <label htmlFor="dashboard-project">Switch project</label>
          <select
            id="dashboard-project"
            className="select"
            value={selected.id}
            onChange={(e) => selectProject(e.target.value)}
          >
            {projects.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </div>
      ) : null}

      <div className="grid-3" style={{ marginBottom: 22 }}>
        <div className="card stat">
          <div className="stat-value">{counts.incidents}</div>
          <div className="stat-label">Incidents</div>
        </div>
        <div className="card stat">
          <div className="stat-value">{counts.analyses}</div>
          <div className="stat-label">Analyses</div>
        </div>
        <div className="card stat">
          <div className="stat-value">{counts.services}</div>
          <div className="stat-label">Services · {counts.repositories} repos</div>
        </div>
      </div>

      <div className="grid-2">
        <section className="card">
          <div className="card-header">
            <h2 className="card-title">Recent incidents</h2>
            <Link to="/incidents" className="small">View all →</Link>
          </div>
          <div className="card-body" style={{ padding: 0 }}>
            {incidents.loading ? (
              <div style={{ padding: 16 }}><SkeletonRows rows={3} /></div>
            ) : incidents.error ? (
              <div style={{ padding: 16 }}><ErrorState message={incidents.error} /></div>
            ) : incidents.data && incidents.data.items.length > 0 ? (
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Title</th>
                    <th>Severity</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {incidents.data.items.map((incident) => (
                    <tr key={incident.id}>
                      <td>
                        <Link className="row-link truncate" to={`/incidents/${incident.id}`}>
                          {incident.title}
                        </Link>
                      </td>
                      <td><SeverityBadge severity={incident.severity} /></td>
                      <td><IncidentStatusBadge status={incident.status} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <div style={{ padding: 16 }}>
                <EmptyState icon="⚠" title="No incidents yet" body="Incidents created for this project appear here." />
              </div>
            )}
          </div>
        </section>

        <section className="card">
          <div className="card-header">
            <h2 className="card-title">Recent analyses</h2>
            <Link to="/analyses" className="small">View all →</Link>
          </div>
          <div className="card-body" style={{ padding: 0 }}>
            {analyses.loading ? (
              <div style={{ padding: 16 }}><SkeletonRows rows={3} /></div>
            ) : analyses.error ? (
              <div style={{ padding: 16 }}><ErrorState message={analyses.error} /></div>
            ) : analyses.data && analyses.data.items.length > 0 ? (
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Type</th>
                    <th>Status</th>
                    <th>Model</th>
                  </tr>
                </thead>
                <tbody>
                  {analyses.data.items.map((run) => (
                    <tr key={run.id}>
                      <td>
                        <Link className="row-link" to={`/analyses/${run.id}`}>
                          {run.type}
                        </Link>
                      </td>
                      <td><AnalysisStatusBadge status={run.status} /></td>
                      <td className="mono muted">{run.model ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <div style={{ padding: 16 }}>
                <EmptyState
                  icon="◫"
                  title="No analyses yet"
                  body="Run an incident investigation or a change-risk analysis to see results here."
                />
              </div>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}
