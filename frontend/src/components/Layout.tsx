import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useProjects } from '../projects/ProjectContext';

function NavIcon({ path }: { path: string }) {
  // Minimal inline glyphs — no icon dependency.
  const glyphs: Record<string, string> = {
    '/': '▦',
    '/incidents': '⚠',
    '/analyses': '◫',
    '/change-risk': '▲',
  };
  return (
    <span className="nav-icon" aria-hidden="true" style={{ textAlign: 'center' }}>
      {glyphs[path] ?? '•'}
    </span>
  );
}

const NAV_ITEMS = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/incidents', label: 'Incidents' },
  { to: '/analyses', label: 'Analyses' },
  { to: '/change-risk', label: 'Change Risk' },
];

export function AppLayout() {
  const { user, logout } = useAuth();
  const { projects, selected, selectProject } = useProjects();
  const navigate = useNavigate();

  const handleLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  const initials = (user?.displayName || user?.email || '?').slice(0, 2).toUpperCase();

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark" aria-hidden="true">C</div>
          <div>
            <div className="brand-name">ChangeLens AI</div>
            <div className="brand-sub">Risk &amp; Incident Intelligence</div>
          </div>
        </div>

        <nav className="nav" aria-label="Main">
          {NAV_ITEMS.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}
            >
              <NavIcon path={item.to} />
              <span>{item.label}</span>
            </NavLink>
          ))}

          <div className="nav-section-label">Context</div>
          <label className="nav-link" htmlFor="project-select" style={{ flexDirection: 'column', alignItems: 'stretch', gap: 6 }}>
            <span>Project</span>
            <select
              id="project-select"
              className="select"
              value={selected?.id ?? ''}
              disabled={projects.length === 0}
              onChange={(e) => selectProject(e.target.value)}
              style={{ background: '#1f2937', color: '#e5e7eb', borderColor: '#374151' }}
            >
              {projects.length === 0 ? (
                <option value="">No projects</option>
              ) : (
                projects.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))
              )}
            </select>
          </label>
        </nav>

        <div className="sidebar-footer">
          Backend-authorized · project isolation enforced server-side
        </div>
      </aside>

      <div className="main">
        <header className="topbar">
          <span className="topbar-project" data-testid="current-project">
            {selected?.name ?? '—'}
          </span>
          <div className="topbar-spacer" />
          <div className="topbar-user">
            <span className="avatar" aria-hidden="true">{initials}</span>
            <span data-testid="current-user">{user?.email}</span>
            <button type="button" className="btn btn-sm" onClick={handleLogout}>
              Log out
            </button>
          </div>
        </header>

        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
