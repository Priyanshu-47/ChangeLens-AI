// TypeScript mirrors of the backend DTOs (wire format is camelCase).
// Single source of truth for the API: backend/src/ChangeLens.Application/Dtos.

// ── Auth ────────────────────────────────────────────────────────────────
export interface User {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
}

export interface AuthResponse {
  accessToken: string;
  expiresInSeconds: number;
  tokenType: string;
  user: User;
}

export interface ProjectMembership {
  projectId: string;
  projectName: string;
  role: string;
}

export interface MeResponse {
  user: User;
  memberships: ProjectMembership[];
}

// ── Projects ────────────────────────────────────────────────────────────
export interface Project {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  memberRole: string;
}

// ── Services / repositories ─────────────────────────────────────────────
export interface Service {
  id: string;
  projectId: string;
  name: string;
  language: string | null;
  rootPath: string | null;
  createdAtUtc: string;
}

export interface Repository {
  id: string;
  projectId: string;
  name: string;
  url: string;
  defaultBranch: string | null;
  language: string;
  createdAtUtc: string;
}

// ── Incidents ───────────────────────────────────────────────────────────
export type IncidentSeverity = 'Sev1' | 'Sev2' | 'Sev3' | 'Sev4' | 'Sev5';
export type IncidentStatus = 'Open' | 'Investigating' | 'Resolved' | 'Closed';
export type IncidentEventType = 'Error' | 'Log' | 'Deployment' | 'Metric';

export interface IncidentEvent {
  id: string;
  occurredAtUtc: string;
  type: IncidentEventType;
  source: string | null;
  message: string | null;
  rawData: unknown;
}

export interface Incident {
  id: string;
  projectId: string;
  title: string;
  severity: IncidentSeverity;
  status: IncidentStatus;
  classification: string | null;
  affectedServiceId: string | null;
  environment: string | null;
  startedAtUtc: string;
  detectedAtUtc: string | null;
  summary: string | null;
  createdAtUtc: string;
  events: IncidentEvent[];
}

export interface CreateIncidentEvent {
  occurredAtUtc?: string;
  type: IncidentEventType;
  source?: string;
  message?: string;
  rawData?: unknown;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

// ── Analyses (async jobs) ───────────────────────────────────────────────
export type AnalysisStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed';
export type AnalysisType = 'ChangeRisk' | 'IncidentInvestigation';

export interface AnalysisError {
  code: string;
  message: string;
}

export interface AnalysisRun {
  id: string;
  projectId: string;
  type: AnalysisType;
  status: AnalysisStatus;
  incidentId: string | null;
  result: IncidentInvestigationResult | ChangeRiskResult | null;
  resultSchemaVersion: string | null;
  model: string | null;
  promptVersion: string | null;
  queuedAtUtc: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  error: AnalysisError | null;
}

export interface InvestigationAccepted {
  analysisId: string;
  status: 'Queued';
  statusUrl: string;
}

// ── Incident investigation result ───────────────────────────────────────
export interface RootCauseCandidate {
  id: string | null;
  title: string;
  confidence: number;
  status: string;
  evidenceIds: string[];
  reasoning: string | null;
  unknowns: string[];
}

export interface Remediation {
  immediateMitigation: string | null;
  investigationSteps: string[];
  recommendedRemediation: string | null;
  validationSteps: string[];
  rollbackConsideration: string | null;
  insufficientEvidence: boolean;
}

export interface IncidentEvidence {
  id: string;
  type: string;
  source: string | null;
  summary: string | null;
  metadata: Record<string, unknown>;
}

export interface IncidentInvestigationResult {
  rootCauseCandidates: RootCauseCandidate[];
  remediation: Remediation;
  unknowns: string[];
  evidence: IncidentEvidence[];
}

// ── Analysis trace (Phase 7 observability) ──────────────────────────────
export interface AnalysisStage {
  name: string;
  status: 'Completed' | 'Failed';
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  durationMs: number | null;
  metadata: Record<string, unknown> | null;
}

export interface RetrievalTraceItem {
  id: string;
  documentType: string;
  title: string | null;
  path: string | null;
  score: number | null;
  /** Semantic similarity — NOT comparable to keyword/dependency ranks. */
  vectorScore: number | null;
  /** 1-based position in the keyword leg's candidate list. */
  keywordRank: number | null;
  /** 1-based position in the dependency leg's candidate list. */
  dependencyRank: number | null;
}

export interface RetrievalTrace {
  queries: string[];
  candidateCount: number;
  selectedCount: number;
  maxChunks: number;
  maxCharsPerChunk: number;
  items: RetrievalTraceItem[];
}

export interface AnalysisTrace {
  analysisId: string;
  type: string;
  status: AnalysisStatus;
  model: string | null;
  promptVersion: string | null;
  resultSchemaVersion: string | null;
  traceSchemaVersion: string | null;
  stages: AnalysisStage[];
  retrieval: RetrievalTrace | null;
  toolCalls: ToolCallTrace[];
  failureCode: string | null;
  failureCategory: string | null;
}

/**
 * One tool call recorded in the analysis trace (Phase 8, docs/agent-tools.md).
 * Status: Proposed | Validated | Executed | Rejected | Failed. Durations are real.
 */
export interface ToolCallTrace {
  toolCallId: string;
  toolName: string;
  status: 'Proposed' | 'Validated' | 'Executed' | 'Rejected' | 'Failed';
  durationMs: number | null;
  arguments: string | null;
  errorCode: string | null;
  evidenceIdCount: number | null;
}

// ── Change-risk result (Workflow A) ─────────────────────────────────────
export type RiskLevel = 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL';

export interface ImpactedComponent {
  componentId: string | null;
  name: string;
  service: string | null;
  filePath: string | null;
  impact: string;
}

export interface EvidenceReference {
  type: string;
  reference: string;
}

export interface RiskFactor {
  id: string | null;
  title: string;
  description: string;
  severity: RiskLevel;
  evidence: EvidenceReference[];
  unknowns: string[];
}

export interface RecommendedTest {
  category: string;
  targetComponent: string | null;
  description: string;
}

export interface ChangeEvidence {
  id: string;
  type: string;
  reference: string;
  summary: string | null;
  aiDocumentId: string | null;
}

export interface ChangeRiskResult {
  riskLevel: RiskLevel;
  confidence: number;
  impactedComponents: ImpactedComponent[];
  riskFactors: RiskFactor[];
  historicalIncidents: unknown[];
  recommendedTests: RecommendedTest[];
  unknowns: string[];
  evidence: ChangeEvidence[];
}

export interface ChangeRiskUsage {
  model: string | null;
  promptVersion: string | null;
  latencyMs: number | null;
  inputTokens: number | null;
  outputTokens: number | null;
  totalTokens: number | null;
  estimatedCostUsd: number | null;
  validationStatus: string;
  repairAttempts: number;
  evidenceTruncated: boolean;
}

export interface ChangeRiskResponse {
  analysisType: string;
  result: ChangeRiskResult;
  usage: ChangeRiskUsage;
  analysisRunId: string | null;
}

// ── Change-risk request ─────────────────────────────────────────────────
export interface ChangedFileInput {
  path: string;
  changeType: 'added' | 'modified' | 'deleted' | 'renamed';
  language?: string;
  symbolsChanged?: string[];
  diffPreview?: string;
}

export interface ChangeRiskRequest {
  projectId: string;
  changeSummary: string;
  changedFiles: ChangedFileInput[];
  repositoryPath?: string;
  baseRevision?: string;
  targetRevision?: string;
}

// ── Error envelope ──────────────────────────────────────────────────────
export interface ApiErrorEnvelope {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  code?: string;
  details?: unknown;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly traceId: string | null;
  readonly details: unknown;

  constructor(message: string, status: number, code: string, traceId: string | null, details: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.traceId = traceId;
    this.details = details;
  }
}
