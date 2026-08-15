using ChangeLens.Application.Dtos;

namespace ChangeLens.Application.Ports;

/// <summary>One resolved changed file with optional base/target content (brief §12–14).</summary>
public sealed record ResolvedChangeFile(
    string Path,
    string ChangeType,
    string? BaseContent,
    string? TargetContent);

/// <summary>Result of resolving a change against a local repository (brief §12–13).</summary>
public sealed record ChangeResolution(
    IReadOnlyList<ResolvedChangeFile> Files,
    string RepositoryPath,
    string? BaseRevision,
    string? TargetRevision,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Resolves base/target content for changed files from a LOCAL, demo-controlled git
/// repository (no GitHub, no webhooks — brief §12). Implementations must validate the
/// repository path (restricted to configured roots), reject path traversal, and never
/// run arbitrary user-supplied shell commands or git arguments (brief §13).
/// </summary>
public interface IChangeSource
{
    ChangeResolution ResolveChange(
        IReadOnlyList<ChangedFileRequest> requestedFiles,
        string? baseRevision,
        string? targetRevision);
}
