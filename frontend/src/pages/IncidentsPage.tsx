import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useProjects } from '../projects/ProjectContext';
import { useAsync } from '../hooks/useAsync';
import { incidentsApi } from '../api/endpoints';
import { EmptyState, ErrorState, IncidentStatusBadge, SeverityBadge, SkeletonRows } from '../components/ui';

export function IncidentsPage() {
  const { selected } = useProjects();
  const projectId = selected?.id;

  const [statusFilter, setStatusFilter] = useState('');
  const [severityFilter, setSeverityFilter] = useState('');
  const [search, setSearch] = useState('');

  const { data, loading, error } = useAsync(
    async () => (projectId ? incidentsApi.list(projectId, { status: statusFilter || undefined, severity: severityFilter || undefined, pageSize: 100 }) : null),
    [projectId, statusFilter, severityFilter],
  );

  const filtered = (data?.items ?? []).filter(
    (incident) =>
      search.trim() === '' ||
      incident.title.toLowerCase().includes(search.trim().toLowerCase()) ||
      (incident.summary ?? '').toLowerCase().includes(search.trim().toLowerCase()),
  );

  return (
    <div>
      <h1 className="page-title">Incidents</h1>
      <p className="page-subtitle">Production incidents in {selected?.name ?? 'the selected project'} — filter and open to investigate.</p>

      <div className="grid-2" style={{ gridTemplateColumns: '1fr 200px 200px', marginBottom: 16 }}>
        <div className="field" style={{ marginBottom: 0 }}>
          <label htmlFor="incident-search">Search</label>
          <input
            id="incident-search"
            className="input"
            type="search"
            placeholder="Title or summary…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div className="field" style={{ marginBottom: 0 }}>
          <label htmlFor="status-filter">Status</label>
          <select id="status-filter" className="select" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="">All</option>
            <option>Open</option>
            <option>Investigating</option>
            <option>Resolved</option>
            <option>Closed</option>
          </select>
        </div>
        <div className="field" style={{ marginBottom: 0 }}>
          <label htmlFor="severity-filter">Severity</label>
          <select id="severity-filter" className="select" value={severityFilter} onChange={(e) => setSeverityFilter(e.target.value)}>
            <option value="">All</option>
            <option>Sev1</option>
            <option>Sev2</option>
            <option>Sev3</option>
            <option>Sev4</option>
            <option>Sev5</option>
          </select>
        </div>
      </div>

      <div className="card">
        {loading ? (
          <div style={{ padding: 16 }}><SkeletonRows rows={5} /></div>
        ) : error ? (
          <div style={{ padding: 16 }}><ErrorState message={error} /></div>
        ) : filtered.length === 0 ? (
          <div style={{ padding: 16 }}>
            <EmptyState
              icon="⚠"
              title={data && data.items.length > 0 ? 'No incidents match the filters' : 'No incidents yet'}
              body={data && data.items.length > 0 ? 'Adjust the search or filters.' : 'Incidents created for this project appear here.'}
            />
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Severity</th>
                <th>Status</th>
                <th>Environment</th>
                <th>Started</th>
                <th>Events</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((incident) => (
                <tr key={incident.id}>
                  <td>
                    <Link className="row-link truncate" to={`/incidents/${incident.id}`}>
                      {incident.title}
                    </Link>
                    <div className="mono faint">{incident.id.slice(0, 8)}…</div>
                  </td>
                  <td><SeverityBadge severity={incident.severity} /></td>
                  <td><IncidentStatusBadge status={incident.status} /></td>
                  <td className="muted">{incident.environment ?? '—'}</td>
                  <td className="mono muted">{new Date(incident.startedAtUtc).toLocaleString()}</td>
                  <td className="muted">{incident.events.length}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
