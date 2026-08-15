import { useState } from 'react';
import { analysesApi } from '../api/endpoints';
import type { AnalysisStage, AnalysisStatus, RetrievalTrace, RetrievalTraceItem } from '../api/types';
import { useAsync } from '../hooks/useAsync';
import { EmptyState, ErrorState, SectionHeading, SkeletonRows } from './ui';

/**
 * Phase 7 observability trace (docs/evaluation.md §5): a collapsible per-stage
 * timeline with real durations, plus a retrieval explorer that shows which chunks
 * entered the prompt and which leg(s) surfaced each one. Vector scores and
 * keyword/dependency ranks are different signals and are shown separately —
 * never summed, never presented as equivalent.
 */
export function TraceSection({ analysisId, status }: { analysisId: string; status: AnalysisStatus }) {
  const [open, setOpen] = useState(false);
  const { data, loading, error, run } = useAsync(
    () => (open ? analysesApi.trace(analysisId) : Promise.resolve(null)),
    [analysisId, open],
  );

  return (
    <section className="section">
      <SectionHeading
        title="Analysis Trace"
        right={
          <button
            type="button"
            className="btn btn-sm"
            onClick={() => setOpen((o) => !o)}
            aria-expanded={open}
            aria-controls="analysis-trace-panel"
          >
            {open ? 'Hide trace' : 'Show trace'}
          </button>
        }
      />

      {open ? (
        <div id="analysis-trace-panel">
          {loading ? (
            <SkeletonRows rows={4} />
          ) : error ? (
            <ErrorState message={error} />
          ) : data ? (
            <TraceBody trace={data} status={status} />
          ) : (
            <EmptyState icon="◫" title="Trace unavailable" />
          )}
          {error ? (
            <button type="button" className="btn btn-sm" style={{ marginTop: 8 }} onClick={run}>
              Retry
            </button>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function TraceBody({ trace, status }: { trace: NonNullable<Awaited<ReturnType<typeof analysesApi.trace>>>; status: AnalysisStatus }) {
  const hasStages = trace.stages.length > 0;
  const inProgress = status === 'Queued' || status === 'Running';

  return (
    <div className="card">
      <div className="card-header">
        <h3 className="card-title">Per-stage timing</h3>
        <span className="small muted">schema {trace.traceSchemaVersion ?? '—'} · {trace.model ?? '—'} · {trace.promptVersion ?? '—'}</span>
      </div>
      <div className="card-body">
        {!hasStages ? (
          inProgress ? (
            <p className="muted small" style={{ margin: 0 }}>
              The trace is written when the analysis completes (the backend reports {status}).
            </p>
          ) : (
            <p className="muted small" style={{ margin: 0 }}>No stage trace was recorded for this analysis.</p>
          )
        ) : (
          <ol className="trace-stages">
            {trace.stages.map((stage, i) => (
              <TraceStageRow key={i} stage={stage} />
            ))}
          </ol>
        )}

        {trace.failureCode ? (
          <div className="error-state" role="alert" style={{ marginTop: 12 }}>
            <div>
              <strong>{trace.failureCode}</strong> · category {trace.failureCategory ?? '—'}
            </div>
          </div>
        ) : null}

        {trace.retrieval ? <RetrievalExplorer retrieval={trace.retrieval} /> : null}
      </div>
    </div>
  );
}

function TraceStageRow({ stage }: { stage: AnalysisStage }) {
  const failed = stage.status === 'Failed';
  return (
    <li className={`trace-stage${failed ? ' failed' : ''}`}>
      <span className="trace-status" aria-hidden="true">{failed ? '✕' : '✓'}</span>
      <span className="trace-name">{stage.name}</span>
      <span className="trace-duration mono">{stage.durationMs != null ? `${stage.durationMs} ms` : '—'}</span>
      {stage.metadata?.failureCategory ? (
        <span className="badge badge-danger">{String(stage.metadata.failureCategory)}</span>
      ) : null}
    </li>
  );
}

/**
 * Retrieval explorer: why the model received these chunks. Candidate vs selected
 * counts answer "what was considered vs what entered the prompt"; per-item leg
 * badges answer "which leg surfaced this chunk".
 */
function RetrievalExplorer({ retrieval }: { retrieval: RetrievalTrace }) {
  return (
    <div className="retrieval-explorer">
      <div className="section-heading" style={{ marginTop: 4 }}>
        <h3 className="card-title">Retrieval explorer</h3>
        <span className="small muted">
          {retrieval.candidateCount} candidates → {retrieval.selectedCount} selected · max{' '}
          {retrieval.maxChunks} chunks · {retrieval.maxCharsPerChunk} chars/chunk
        </span>
      </div>

      {retrieval.queries.length > 0 ? (
        <div className="field" style={{ marginBottom: 8 }}>
          <label>Queries</label>
          <ul className="query-list">
            {retrieval.queries.map((query, i) => (
              <li key={i} className="mono small">{query}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {retrieval.items.length === 0 ? (
        <p className="muted small">No retrieval items were recorded for this analysis.</p>
      ) : (
        <ul className="trace-items">
          {retrieval.items.map((item) => (
            <TraceItemRow key={item.id} item={item} />
          ))}
        </ul>
      )}

      <p className="small muted" style={{ margin: '8px 0 0' }}>
        Vector scores are semantic similarities; keyword and dependency ranks are 1-based
        positions in that leg's candidate list. These are different signals and are not
        directly comparable.
      </p>
    </div>
  );
}

function TraceItemRow({ item }: { item: RetrievalTraceItem }) {
  const legs: { label: string; value: string; tone: string }[] = [];
  if (item.vectorScore != null) {
    legs.push({ label: 'vector', value: item.vectorScore.toFixed(3), tone: 'badge-info' });
  }
  if (item.keywordRank != null) {
    legs.push({ label: 'keyword', value: `#${item.keywordRank}`, tone: 'badge-muted' });
  }
  if (item.dependencyRank != null) {
    legs.push({ label: 'dependency', value: `#${item.dependencyRank}`, tone: 'badge-warning' });
  }

  return (
    <li className="trace-item">
      <div style={{ minWidth: 0 }}>
        <div className="evidence-head">
          <span className="evidence-id">{item.id}</span>
          {item.documentType ? <span className="badge badge-muted">{item.documentType}</span> : null}
        </div>
        {item.path ? <div className="mono faint small truncate">{item.path}</div> : null}
        {item.title && item.title !== item.path ? <div className="small muted truncate">{item.title}</div> : null}
      </div>
      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'flex-start', justifyContent: 'flex-end' }}>
        {legs.map((leg) => (
          <span key={leg.label} className={`badge ${leg.tone}`} title={`Surfaced by the ${leg.label} leg`}>
            {leg.label} {leg.value}
          </span>
        ))}
      </div>
    </li>
  );
}
