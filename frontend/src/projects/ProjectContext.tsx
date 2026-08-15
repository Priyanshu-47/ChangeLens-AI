import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { projectsApi } from '../api/endpoints';
import type { Project } from '../api/types';

const SELECTED_PROJECT_KEY = 'changelens.selectedProjectId';

interface ProjectState {
  projects: Project[];
  /** True while the project list is loading. */
  loading: boolean;
  error: string | null;
  selected: Project | null;
  selectProject: (id: string) => void;
  reload: () => Promise<void>;
}

const ProjectContext = createContext<ProjectState | null>(null);

export function ProjectProvider({ children }: { children: ReactNode }) {
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(() =>
    window.localStorage.getItem(SELECTED_PROJECT_KEY),
  );

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const page = await projectsApi.list();
      setProjects(page.items);
      // If the remembered selection no longer exists, fall back to the first project.
      setSelectedId((current) => {
        if (current && page.items.some((p) => p.id === current)) {
          return current;
        }
        return page.items[0]?.id ?? null;
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load projects.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const selectProject = useCallback((id: string) => {
    window.localStorage.setItem(SELECTED_PROJECT_KEY, id);
    setSelectedId(id);
  }, []);

  // The backend remains the authorization authority — the locally remembered
  // selection is only a UX convenience; every request is scoped server-side.
  const selected = useMemo(
    () => projects.find((p) => p.id === selectedId) ?? null,
    [projects, selectedId],
  );

  const value = useMemo<ProjectState>(
    () => ({ projects, loading, error, selected, selectProject, reload }),
    [projects, loading, error, selected, selectProject, reload],
  );

  return <ProjectContext.Provider value={value}>{children}</ProjectContext.Provider>;
}

export function useProjects(): ProjectState {
  const ctx = useContext(ProjectContext);
  if (!ctx) {
    throw new Error('useProjects must be used within a ProjectProvider');
  }
  return ctx;
}
