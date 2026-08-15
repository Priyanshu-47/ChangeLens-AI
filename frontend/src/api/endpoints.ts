import { api } from './client';
import type {
  AnalysisRun,
  AnalysisTrace,
  AuthResponse,
  ChangeRiskRequest,
  ChangeRiskResponse,
  Incident,
  InvestigationAccepted,
  MeResponse,
  Paged,
  Project,
  Repository,
  Service,
} from './types';

// ── auth ────────────────────────────────────────────────────────────────
export const authApi = {
  login(email: string, password: string) {
    return api<AuthResponse>('/auth/login', { method: 'POST', body: { email, password }, auth: false });
  },
  me() {
    return api<MeResponse>('/auth/me');
  },
  logout() {
    // The backend has no session to revoke for JWT; clearing the local token is the
    // client-side logout. (A token blacklist is out of MVP scope.)
  },
};

// ── projects ────────────────────────────────────────────────────────────
export const projectsApi = {
  list(page = 1, pageSize = 50) {
    return api<Paged<Project>>('/projects', { query: { page, pageSize } });
  },
  get(projectId: string) {
    return api<Project>(`/projects/${projectId}`);
  },
};

// ── services / repositories ─────────────────────────────────────────────
export const codeModelApi = {
  services(projectId: string) {
    return api<Paged<Service>>(`/projects/${projectId}/services`, { query: { page: 1, pageSize: 100 } });
  },
  repositories(projectId: string) {
    return api<Paged<Repository>>(`/projects/${projectId}/repositories`, { query: { page: 1, pageSize: 100 } });
  },
};

// ── incidents ───────────────────────────────────────────────────────────
export const incidentsApi = {
  list(projectId: string, options: { status?: string; severity?: string; page?: number; pageSize?: number } = {}) {
    return api<Paged<Incident>>('/incidents', {
      query: {
        projectId,
        status: options.status,
        severity: options.severity,
        page: options.page ?? 1,
        pageSize: options.pageSize ?? 25,
      },
    });
  },
  get(incidentId: string) {
    return api<Incident>(`/incidents/${incidentId}`);
  },
  investigate(incidentId: string, requestId?: string) {
    return api<InvestigationAccepted>(`/incidents/${incidentId}/investigate`, {
      method: 'POST',
      body: requestId ? { requestId } : {},
    });
  },
};

// ── analyses ────────────────────────────────────────────────────────────
export const analysesApi = {
  get(analysisId: string) {
    return api<AnalysisRun>(`/analyses/${analysisId}`);
  },
  trace(analysisId: string) {
    return api<AnalysisTrace>(`/analyses/${analysisId}/trace`);
  },
  list(
    projectId: string,
    options: { type?: string; status?: string; incidentId?: string; page?: number; pageSize?: number } = {},
  ) {
    return api<Paged<AnalysisRun>>('/analyses', {
      query: {
        projectId,
        type: options.type,
        status: options.status,
        incidentId: options.incidentId,
        page: options.page ?? 1,
        pageSize: options.pageSize ?? 25,
      },
    });
  },
};

// ── change risk (Workflow A) ────────────────────────────────────────────
export const changeRiskApi = {
  analyze(request: ChangeRiskRequest) {
    return api<ChangeRiskResponse>('/analyses/change-risk', { method: 'POST', body: request });
  },
};
