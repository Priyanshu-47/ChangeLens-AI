namespace ChangeLens.Infrastructure.Options;

/// <summary>
/// Configuration for the local, demo-controlled git change source (brief §12–13).
/// The repository is restricted to a configured allowed root; nothing outside it can
/// be read. When RootPath is empty it is auto-discovered by walking up from the app
/// base directory looking for data/demo-repository.
/// </summary>
public sealed class ChangeSourceOptions
{
    public const string SectionName = "ChangeSource";

    /// <summary>Absolute path of the allowed root (default: auto-discovered workspace root).</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Repository path relative to RootPath (default: the AcmePay demo repository).</summary>
    public string RepositoryPath { get; set; } = "data/demo-repository";

    /// <summary>
    /// Base revision for analyses that do not supply one (the Phase 3 commit hash is
    /// stable — it is never rewritten). When null, HEAD is used.
    /// </summary>
    public string? DefaultBaseRevision { get; set; }

    /// <summary>Impact traversal depth for the dependency graph (brief §15).</summary>
    public int MaxImpactDepth { get; set; } = 2;

    /// <summary>Per-git-command timeout.</summary>
    public int GitTimeoutSeconds { get; set; } = 15;
}
