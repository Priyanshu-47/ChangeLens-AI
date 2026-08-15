using System.Diagnostics;
using ChangeLens.Application.Dtos;

namespace ChangeLens.Application.Tracing;

/// <summary>
/// Records the per-stage observability trace of one analysis (docs/evaluation.md §5).
/// Durations are real wall-clock measurements (Stopwatch), never estimates; stages the
/// host cannot observe are not invented. The AI service's own retrieval trace is attached
/// verbatim, and failure state is recorded with a normalized category — raw prompts,
/// tokens, and secrets are never stored.
/// </summary>
public sealed class AnalysisTraceBuilder
{
    private const string SchemaVersion = "trace-v1";

    private readonly List<AnalysisStageDto> _stages = [];
    private readonly List<ToolCallTraceDto> _toolCalls = [];
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private string? _failureCategory;
    private string? _failureCode;

    public IReadOnlyList<AnalysisStageDto> Stages => _stages;

    public IReadOnlyList<ToolCallTraceDto> ToolCalls => _toolCalls;

    public string? FailureCategory => _failureCategory;

    public string? FailureCode => _failureCode;

    public RetrievalTraceDto? Retrieval { get; private set; }

    public string Schema => SchemaVersion;

    /// <summary>Begins a timed stage; the returned scope records it on Dispose.</summary>
    public IDisposable Stage(string name)
    {
        var scope = new StageScope(this, name);
        return scope;
    }

    /// <summary>Attaches the AI service's retrieval/evidence-selection trace.</summary>
    public void SetRetrieval(RetrievalTraceDto? retrieval) => Retrieval = retrieval;

    /// <summary>Records one tool call (Phase 8). Args are a truncated identifier-only summary.</summary>
    public void AddToolCall(
        string toolCallId,
        string toolName,
        string status,
        long? durationMs,
        string? arguments,
        string? errorCode,
        int? evidenceIdCount)
    {
        _toolCalls.Add(new ToolCallTraceDto
        {
            ToolCallId = toolCallId,
            ToolName = toolName,
            Status = status,
            DurationMs = durationMs,
            Arguments = arguments,
            ErrorCode = errorCode,
            EvidenceIdCount = evidenceIdCount
        });
    }

    /// <summary>Marks the most recent stage Failed and records the normalized category.</summary>
    public void Fail(string failureCode, string message)
    {
        _failureCode = failureCode;
        _failureCategory = AnalysisFailureCategory.For(failureCode);
        if (_stages.Count > 0)
        {
            var last = _stages[^1];
            last.Status = "Failed";
            last.Metadata ??= new Dictionary<string, object?>();
            last.Metadata["failureCode"] = failureCode;
            last.Metadata["failureCategory"] = _failureCategory;
            last.Metadata["message"] = message;
        }
    }

    /// <summary>Serializes the trace to the persisted JSON (camelCase).</summary>
    public string Serialize()
    {
        var payload = new
        {
            schemaVersion = Schema,
            totalDurationMs = _stopwatch.ElapsedMilliseconds,
            stages = _stages,
            retrieval = Retrieval,
            toolCalls = _toolCalls,
            failure = _failureCode is null
                ? null
                : new { code = _failureCode, category = _failureCategory }
        };
        return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    private sealed class StageScope(AnalysisTraceBuilder owner, string name) : IDisposable
    {
        private readonly DateTime _started = DateTime.UtcNow;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _sw.Stop();
            owner._stages.Add(new AnalysisStageDto
            {
                Name = name,
                Status = "Completed",
                StartedAtUtc = _started,
                CompletedAtUtc = DateTime.UtcNow,
                DurationMs = _sw.ElapsedMilliseconds
            });
        }
    }
}
