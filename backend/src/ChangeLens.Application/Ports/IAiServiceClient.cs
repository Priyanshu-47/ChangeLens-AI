using ChangeLens.Application.Dtos;

namespace ChangeLens.Application.Ports;

/// <summary>
/// Typed client for the internal Python AI service. The .NET backend is the only
/// caller; it authenticates with the shared internal key, propagates the correlation
/// id, and maps AI-service errors to <see cref="Exceptions.ChangeLensException"/>s.
/// Dependency direction stays clean: Application depends on this port, the
/// Infrastructure implementation talks HTTP (ADR-0002).
/// </summary>
public interface IAiServiceClient
{
    /// <summary>
    /// Runs the structured change-risk analysis (POST /internal/v1/analysis/risk)
    /// and returns the already-validated result plus usage metadata.
    /// </summary>
    Task<ChangeRiskAnalysisResponse> AnalyzeChangeRiskAsync(
        AnalyzeChangeRiskRequest request, CancellationToken ct);

    /// <summary>
    /// Runs the structured incident investigation (POST /internal/v1/analysis/incident)
    /// and returns the already-validated result plus usage metadata.
    /// </summary>
    Task<IncidentAnalysisResponseDto> AnalyzeIncidentAsync(
        IncidentAnalysisRequestDto request, CancellationToken ct);
}
