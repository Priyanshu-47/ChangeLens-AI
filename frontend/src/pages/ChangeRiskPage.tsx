import { useState, type FormEvent } from 'react';
import { useProjects } from '../projects/ProjectContext';
import { changeRiskApi } from '../api/endpoints';
import type { ChangeRiskResponse, RiskFactor } from '../api/types';
import { ErrorState, RiskBadge, SectionHeading, SkeletonRows } from '../components/ui';
import { DistinctionBanner } from '../components/Investigation';

interface FileRow {
  path: string;
  changeType: 'added' | 'modified' | 'deleted' | 'renamed';
  language: string;
}

function EmptyRow(): FileRow {
  return { path: '', changeType: 'modified', language: 'csharp' };
}

export function ChangeRiskPage() {
  const { selected } = useProjects();
  const projectId = selected?.id;

  const [summary, setSummary] = useState('');
  const [repositoryPath, setRepositoryPath] = useState('data/demo-repository');
  const [baseRevision, setBaseRevision] = useState('HEAD');
  const [targetRevision, setTargetRevision] = useState('');
  const [files, setFiles] = useState<FileRow[]>([EmptyRow()]);

  const [result, setResult] = useState<ChangeRiskResponse | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const updateFile = (index: number, patch: Partial<FileRow>) => {
    setFiles((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  };

  const addFile = () => setFiles((current) => [...current, EmptyRow()]);
  const removeFile = (index: number) => setFiles((current) => current.filter((_, i) => i !== index));

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    setResult(null);
    try {
      const changedFiles = files
        .filter((row) => row.path.trim().length > 0)
        .map((row) => ({
          path: row.path.trim(),
          changeType: row.changeType,
          language: row.language.trim() || undefined,
        }));

      const response = await changeRiskApi.analyze({
        projectId: projectId!,
        changeSummary: summary.trim(),
        changedFiles,
        repositoryPath: repositoryPath.trim() || undefined,
        baseRevision: baseRevision.trim() || undefined,
        targetRevision: targetRevision.trim() || undefined,
      });
      setResult(response);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Change-risk analysis failed.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div>
      <h1 className="page-title">Change Risk</h1>
      <p className="page-subtitle">
        Workflow A — the backend resolves the change (safe local git + Roslyn), builds the
        dependency graph, retrieves evidence, and returns a grounded risk report.
      </p>

      {!projectId ? (
        <ErrorState message="Select a project before running a change-risk analysis." />
      ) : (
        <div className="grid-2" style={{ gridTemplateColumns: 'minmax(360px, 460px) 1fr', alignItems: 'start' }}>
          <form className="card" onSubmit={handleSubmit}>
            <div className="card-header">
              <h2 className="card-title">Submit a change</h2>
            </div>
            <div className="card-body">
              <div className="field">
                <label htmlFor="change-summary">Change summary</label>
                <textarea
                  id="change-summary"
                  className="textarea"
                  value={summary}
                  onChange={(e) => setSummary(e.target.value)}
                  placeholder="e.g. Key-rotation observability: extract signing-key parsing and expose the current key fingerprint for monitoring."
                  required
                />
              </div>

              <div className="grid-2" style={{ gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <div className="field">
                  <label htmlFor="repo-path">Repository path</label>
                  <input
                    id="repo-path"
                    className="input"
                    value={repositoryPath}
                    onChange={(e) => setRepositoryPath(e.target.value)}
                    placeholder="data/demo-repository"
                  />
                </div>
                <div className="field">
                  <label htmlFor="base-rev">Base revision</label>
                  <input
                    id="base-rev"
                    className="input mono"
                    value={baseRevision}
                    onChange={(e) => setBaseRevision(e.target.value)}
                    placeholder="HEAD"
                  />
                </div>
              </div>
              <div className="field">
                <label htmlFor="target-rev">Target revision (blank = working tree)</label>
                <input
                  id="target-rev"
                  className="input mono"
                  value={targetRevision}
                  onChange={(e) => setTargetRevision(e.target.value)}
                  placeholder="(working tree)"
                />
              </div>

              <div className="section-heading" style={{ marginTop: 4 }}>
                <h2 className="card-title">Changed files</h2>
                <button type="button" className="btn btn-sm" onClick={addFile}>+ Add file</button>
              </div>

              {files.map((row, index) => (
                <div key={index} className="grid-2" style={{ gridTemplateColumns: '1fr 120px 90px', gap: 8, marginBottom: 8 }}>
                  <input
                    className="input mono"
                    value={row.path}
                    onChange={(e) => updateFile(index, { path: e.target.value })}
                    placeholder="src/AcmePay.Application/Auth/TokenService.cs"
                    aria-label={`File path ${index + 1}`}
                  />
                  <select
                    className="select"
                    value={row.changeType}
                    onChange={(e) => updateFile(index, { changeType: e.target.value as FileRow['changeType'] })}
                    aria-label={`Change type ${index + 1}`}
                  >
                    <option value="modified">modified</option>
                    <option value="added">added</option>
                    <option value="deleted">deleted</option>
                    <option value="renamed">renamed</option>
                  </select>
                  <input
                    className="input"
                    value={row.language}
                    onChange={(e) => updateFile(index, { language: e.target.value })}
                    placeholder="csharp"
                    aria-label={`Language ${index + 1}`}
                  />
                </div>
              ))}
              {files.length > 1 ? (
                <button type="button" className="btn btn-sm btn-danger" onClick={() => removeFile(files.length - 1)}>
                  Remove last file
                </button>
              ) : null}

              {error ? <ErrorState message={error} /> : null}

              <div className="form-actions" style={{ marginTop: 16 }}>
                <button type="submit" className="btn btn-primary" disabled={submitting || files.every((row) => row.path.trim() === '')}>
                  {submitting ? 'Analyzing…' : 'Run change-risk analysis'}
                </button>
              </div>
            </div>
          </form>

          <div>
            {submitting ? <SkeletonRows rows={8} /> : null}
            {result ? <ChangeRiskResultView response={result} /> : null}
            {!submitting && !result ? (
              <div className="card card-body">
                <p className="muted small" style={{ margin: 0 }}>
                  Submit a change to see the risk report: risk level, confidence, changed and
                  impacted symbols, dependency paths, risk factors, and grounded evidence.
                </p>
              </div>
            ) : null}
          </div>
        </div>
      )}
    </div>
  );
}

function ChangeRiskResultView({ response }: { response: ChangeRiskResponse }) {
  const { result, usage } = response;

  return (
    <div>
      <div className="card analysis-hero">
        <div className="analysis-status-icon" aria-hidden="true">✓</div>
        <div style={{ flex: 1 }}>
          <div className="small muted">RELEASE RISK</div>
          <div style={{ display: 'flex', gap: 12, alignItems: 'center', marginTop: 4 }}>
            <RiskBadge level={result.riskLevel} />
            <span className="small muted">Confidence {Math.round(result.confidence * 100)}%</span>
          </div>
        </div>
        <div className="mono faint">{usage.model ?? '—'} · {usage.promptVersion ?? '—'}</div>
      </div>

      <div className="meta-grid" style={{ marginTop: 16 }}>
        <div className="meta-item">
          <div className="meta-label">Impacted components</div>
          <div className="meta-value">{result.impactedComponents.length}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Risk factors</div>
          <div className="meta-value">{result.riskFactors.length}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Evidence items</div>
          <div className="meta-value">{result.evidence.length}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Recommended tests</div>
          <div className="meta-value">{result.recommendedTests.length}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Latency</div>
          <div className="meta-value">{usage.latencyMs != null ? `${usage.latencyMs} ms` : '—'}</div>
        </div>
        <div className="meta-item">
          <div className="meta-label">Validation</div>
          <div className="meta-value">{usage.validationStatus ?? '—'}</div>
        </div>
      </div>

      {result.impactedComponents.length > 0 ? (
        <section className="section">
          <SectionHeading title="Impacted components" />
          <div className="card card-body">
            <ul className="checklist">
              {result.impactedComponents.map((component, i) => (
                <li key={i}>
                  <span className="badge badge-muted" style={{ marginRight: 8 }}>{component.impact}</span>
                  <strong>{component.name}</strong>
                  {component.service ? <span className="muted"> · {component.service}</span> : null}
                  {component.filePath ? <span className="mono faint"> · {component.filePath}</span> : null}
                </li>
              ))}
            </ul>
          </div>
        </section>
      ) : null}

      <section className="section">
        <SectionHeading title="Risk factors" />
        {result.riskFactors.length === 0 ? (
          <p className="muted small">No risk factors identified.</p>
        ) : (
          result.riskFactors.map((factor, index) => <RiskFactorCard key={factor.id ?? index} factor={factor} />)
        )}
      </section>

      <section className="section">
        <SectionHeading title="Evidence" />
        <DistinctionBanner />
        <div className="evidence-list">
          {result.evidence.length === 0 ? (
            <p className="muted small">No evidence items returned.</p>
          ) : (
            result.evidence.map((item) => (
              <div key={item.id} className="evidence-card" id={`evidence-${item.id.replace(/[^a-zA-Z0-9_-]/g, '_')}`}>
                <div className="evidence-head">
                  <span className="badge badge-accent">{item.type}</span>
                  <span className="evidence-id">{item.id}</span>
                </div>
                {item.summary ? <p className="evidence-summary">{item.summary}</p> : null}
                {item.reference ? <div className="evidence-meta">reference: {item.reference}</div> : null}
              </div>
            ))
          )}
        </div>
      </section>

      {result.unknowns.length > 0 ? (
        <section className="section">
          <SectionHeading title="Unknown / missing information" />
          <div className="unknown-block">
            <ul className="unknown-list">
              {result.unknowns.map((u, i) => (
                <li key={i}>{u}</li>
              ))}
            </ul>
          </div>
        </section>
      ) : null}

      {result.recommendedTests.length > 0 ? (
        <section className="section">
          <SectionHeading title="Recommended tests" />
          <div className="card card-body">
            <ul className="checklist">
              {result.recommendedTests.map((test, i) => (
                <li key={i}>
                  <span className="badge badge-muted" style={{ marginRight: 8 }}>{test.category}</span>
                  {test.description}
                </li>
              ))}
            </ul>
          </div>
        </section>
      ) : null}
    </div>
  );
}

function RiskFactorCard({ factor }: { factor: RiskFactor }) {
  return (
    <div className="candidate" style={{ marginBottom: 10 }}>
      <div className="candidate-header" style={{ cursor: 'default' }}>
        <span className="candidate-rank" aria-hidden="true" style={{ background: 'var(--warning-soft)', color: 'var(--warning)' }}>
          !
        </span>
        <span className="candidate-title">{factor.title}</span>
        <span className="candidate-meta">
          <RiskBadge level={factor.severity} />
          <span>{factor.evidence.length} evidence refs</span>
        </span>
      </div>
      <div className="candidate-body">
        <p style={{ margin: '0 0 10px' }}>{factor.description}</p>
        <div className="small muted">Evidence references:</div>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 6 }}>
          {factor.evidence.map((ref, i) => (
            <span key={i} className="link-chip" style={{ cursor: 'default' }}>
              {ref.reference}
            </span>
          ))}
        </div>
        {factor.unknowns.length > 0 ? (
          <div className="small" style={{ marginTop: 10, color: 'var(--warning)' }}>
            Unknowns: {factor.unknowns.join(' · ')}
          </div>
        ) : null}
      </div>
    </div>
  );
}
