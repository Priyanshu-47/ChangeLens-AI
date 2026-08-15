using ChangeLens.Application.Dtos;

namespace ChangeLens.Application.Ports;

/// <summary>
/// Change-intelligence engine port (ADR-0011: Roslyn lives in Infrastructure).
/// Given a change request it resolves the base/target repository states, runs the
/// Roslyn analyzer, builds the dependency graph, and produces the normalized change
/// model that steers retrieval and the AI request. It never executes analyzed code.
/// </summary>
public interface IChangeAnalysisEngine
{
    /// <summary>
    /// Build the change model for a request. Degrades gracefully: files that cannot be
    /// resolved (not in the repository, missing git) produce warnings, not failures —
    /// the AI service still runs on the supplied change summary and paths.
    /// </summary>
    ChangeModelDto BuildChangeModel(AnalyzeChangeRiskRequest request);
}
