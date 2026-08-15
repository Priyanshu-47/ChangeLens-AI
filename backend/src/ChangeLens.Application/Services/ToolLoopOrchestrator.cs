using System.Diagnostics;
using System.Text.Json;
using ChangeLens.Application.Configuration;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Tools;
using ChangeLens.Application.Tracing;
using ChangeLens.Domain.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChangeLens.Application.Services;

/// <summary>
/// The controlled tool loop (docs/agent-tools.md §2, ADR-0013):
///
///   AI turn → tool proposal? ─no→ final result
///                         └─yes→ registry lookup → argument validation → project
///                                 authorization → execution (bounded timeout) →
///                                 sanitized result fed back → next AI turn
///
/// "AI proposes; the application validates, authorizes, and executes." The loop is
/// bounded (MaxToolCalls); each call is audited and recorded in the trace. Unknown
/// tools, invalid arguments, and cross-project lookups become safe structured tool
/// results the model may reason from — never raw exceptions and never retried blindly.
/// </summary>
public sealed class ToolLoopOrchestrator(
    ToolRegistry registry,
    IAiServiceClient aiClient,
    AuditLogService audit,
    IOptions<AnalysisOptions> options,
    ILogger<ToolLoopOrchestrator> logger)
{
    private static readonly JsonSerializerOptions ArgJson = new(JsonSerializerDefaults.Web);

    private AnalysisOptions Opts => options.Value;

    /// <summary>
    /// Runs the loop until the AI returns a final result or the safety limit is hit.
    /// The request is mutated (tool catalog + accumulated tool results) between turns.
    /// </summary>
    public async Task<IncidentAnalysisResponseDto> ExecuteAsync(
        IncidentAnalysisRequestDto request,
        ToolExecutionContext context,
        AnalysisTraceBuilder trace,
        CancellationToken ct)
    {
        request.ToolCatalog = registry.Describe();
        request.PromptVersion = "incident-tools-v1";

        var results = new List<ToolResultItemDto>();
        var maxCalls = Math.Max(1, Opts.MaxToolCalls);

        for (var call = 0; call <= maxCalls; call++)
        {
            request.ToolResults = results;
            var response = await aiClient.AnalyzeIncidentAsync(request, ct);

            if (!string.Equals(response.Kind, "tool_call", StringComparison.Ordinal)
                || response.ToolCall is null)
            {
                return response; // final turn — the result is already validated by the AI service
            }

            if (call >= maxCalls)
            {
                var args = JsonSerializer.SerializeToElement(response.ToolCall.Arguments, ArgJson);
                trace.AddToolCall(
                    response.ToolCall.Id, response.ToolCall.Name, "Rejected", 0,
                    ToolArguments.Summarize(args), ToolErrorCode.NotAllowed, null);
                throw new ToolCallLimitExceededException(maxCalls);
            }

            results.Add(await ExecuteToolAsync(context, response.ToolCall, trace, ct));
        }

        throw new ToolCallLimitExceededException(maxCalls); // unreachable; defensive
    }

    private async Task<ToolResultItemDto> ExecuteToolAsync(
        ToolExecutionContext context, ToolCallDto toolCall, AnalysisTraceBuilder trace, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var arguments = JsonSerializer.SerializeToElement(toolCall.Arguments, ArgJson);
        var argsSummary = ToolArguments.Summarize(arguments);

        var tool = registry.TryGet(toolCall.Name);
        if (tool is null)
        {
            // Unknown tool: rejected with TOOL_NOT_ALLOWED, fed back to the model
            // (docs/agent-tools.md §4). Never an exception — the loop stays in control.
            var result = ToolExecutionResult.Rejected(
                ToolErrorCode.NotAllowed, "Tool is not in the allowlist.");
            await RecordAsync(context, trace, started, toolCall, result, ToolStatus.NotAllowed, argsSummary);
            return ToResultItem(toolCall, result, ToolStatus.NotAllowed);
        }

        ToolExecutionResult execution;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Opts.ToolTimeoutSeconds)));
            execution = await tool.ExecuteAsync(context, arguments, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Tool {Tool} exceeded the {Timeout}s tool timeout (analysis {AnalysisRunId})",
                toolCall.Name, Opts.ToolTimeoutSeconds, context.AnalysisRunId);
            execution = new ToolExecutionResult(
                ToolStatus.Timeout, null, [], ErrorCode: ToolErrorCode.Timeout, DurationMs: started.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Tool {Tool} failed unexpectedly (analysis {AnalysisRunId})",
                toolCall.Name, context.AnalysisRunId);
            execution = ToolExecutionResult.Failed(
                ToolErrorCode.ToolError, "The tool failed to execute.");
        }

        await RecordAsync(context, trace, started, toolCall, execution, execution.Status, argsSummary);
        return ToResultItem(toolCall, execution, execution.Status);
    }

    /// <summary>Audits the call and records it in the trace (sanitized, no secrets).</summary>
    private async Task RecordAsync(
        ToolExecutionContext context,
        AnalysisTraceBuilder trace,
        Stopwatch started,
        ToolCallDto toolCall,
        ToolExecutionResult result,
        ToolStatus status,
        string argsSummary)
    {
        started.Stop();
        var executed = status == ToolStatus.Executed;
        var traceStatus = executed ? "Executed"
            : status is ToolStatus.Rejected or ToolStatus.NotAllowed ? "Rejected"
            : "Failed";

        trace.AddToolCall(
            toolCall.Id, toolCall.Name, traceStatus, started.ElapsedMilliseconds,
            argsSummary, result.ErrorCode, result.EvidenceIds.Count);

        await audit.WriteAsync(
            executed ? AuditActions.ToolExecuted : AuditActions.ToolRejected,
            "tool", null, context.ProjectId, context.AnalysisRunId,
            details: new
            {
                analysisRunId = context.AnalysisRunId,
                toolCallId = toolCall.Id,
                toolName = toolCall.Name,
                status = StatusToWire(status),
                durationMs = started.ElapsedMilliseconds,
                failureCode = result.ErrorCode,
                evidenceIdCount = result.EvidenceIds.Count
            },
            ct: context.CancellationToken);

        logger.LogInformation(
            "Tool {Tool} {Status} for analysis {AnalysisRunId} in {Duration}ms" +
            (result.ErrorCode is null ? "" : " ({ErrorCode})"),
            toolCall.Name, StatusToWire(status), context.AnalysisRunId,
            started.ElapsedMilliseconds, result.ErrorCode);
    }

    private ToolResultItemDto ToResultItem(ToolCallDto toolCall, ToolExecutionResult result, ToolStatus status)
        => new()
        {
            ToolCallId = toolCall.Id,
            ToolName = toolCall.Name,
            Status = StatusToWire(status),
            Output = TruncateOutput(result.OutputJson),
            ErrorCode = result.ErrorCode
        };

    private static string StatusToWire(ToolStatus status) => status switch
    {
        ToolStatus.Executed => "executed",
        ToolStatus.Rejected => "rejected",
        ToolStatus.Failed => "failed",
        ToolStatus.NotAllowed => "not_allowed",
        ToolStatus.Timeout => "timeout",
        _ => "failed"
    };

    /// <summary>Bounds what is fed back to the model (tool output is untrusted data).</summary>
    private static string? TruncateOutput(string? output)
        => output is null ? null : output.Length <= 60_000 ? output : output[..60_000];
}
