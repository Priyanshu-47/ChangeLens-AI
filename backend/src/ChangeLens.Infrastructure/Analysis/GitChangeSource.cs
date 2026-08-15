using System.Diagnostics;
using System.Text.RegularExpressions;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;
using ChangeLens.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChangeLens.Infrastructure.Analysis;

/// <summary>
/// Resolves base/target content for changed files from a LOCAL git repository
/// (brief §12). Security constraints (brief §13):
///  - the repository must resolve inside the configured allowed root;
///  - file paths are validated: relative, no traversal, must stay inside the repo;
///  - git revisions are validated against a strict pattern and never start with '-'
///    (so they cannot be interpreted as git flags);
///  - git is invoked with a fixed argument list (no shell), so no user-supplied
///    command can be executed; the analyzer only parses/analyzes source (ADR-0011).
/// </summary>
public sealed class GitChangeSource(
    IOptions<ChangeSourceOptions> options,
    ILogger<GitChangeSource> logger) : IChangeSource
{
    /// <summary>Revisions: alphanumeric start, then [A-Za-z0-9._~^/-], no '..' (no parent traversal).</summary>
    private static readonly Regex SafeRevision = new(
        "^(?!.*\\.\\.)[A-Za-z0-9][A-Za-z0-9._~^/-]*$", RegexOptions.Compiled);

    public ChangeResolution ResolveChange(
        IReadOnlyList<ChangedFileRequest> requestedFiles,
        string? baseRevision,
        string? targetRevision)
    {
        var root = ResolveRootPath();
        var repoRel = ValidateRelativePath(options.Value.RepositoryPath, root, "repository path");
        if (repoRel is not null)
        {
            // RepositoryPath is server configuration, not user input, but fail loudly
            // rather than silently resolving against an unexpected directory.
            throw new InvalidOperationException($"ChangeSource:RepositoryPath is invalid: {repoRel}");
        }

        var repoDir = Path.GetFullPath(Path.Combine(root, options.Value.RepositoryPath));
        var warnings = new List<string>();

        // git show resolves paths relative to the repository toplevel, which may differ
        // from RepositoryPath (e.g. a repo subdirectory of the workspace). Resolve it
        // once and verify it stays inside the allowed root.
        var topLevel = ResolveTopLevel(repoDir);
        if (topLevel is null || !IsWithinRoot(topLevel, root))
        {
            warnings.Add("Repository toplevel could not be validated inside the allowed root; using the repository directory as git root.");
            topLevel = repoDir;
        }

        var baseRev = ResolveRevision(baseRevision, options.Value.DefaultBaseRevision, "HEAD", warnings);
        var targetRev = ResolveRevision(targetRevision, null, null, warnings);

        var resolved = new List<ResolvedChangeFile>();
        foreach (var file in requestedFiles)
        {
            var path = file.Path.Replace('\\', '/');
            var validation = ValidateRelativePath(path, repoDir, "file path");
            if (validation is not null)
            {
                warnings.Add(validation);
                continue;
            }

            var gitPath = Path.GetRelativePath(topLevel, Path.Combine(repoDir, path))
                .Replace('\\', '/');
            var targetContent = ReadTargetContent(repoDir, path, targetRev, gitPath, warnings);
            var baseContent = ReadBaseContent(root, baseRev, gitPath, warnings);

            resolved.Add(new ResolvedChangeFile(path, file.ChangeType, baseContent, targetContent));
        }

        if (warnings.Count > 0)
        {
            foreach (var w in warnings.Take(10))
            {
                logger.LogWarning("Change source: {Warning}", w);
            }
        }

        return new ChangeResolution(resolved, repoDir, baseRev, targetRev, warnings);
    }

    private string ResolveRootPath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.RootPath))
        {
            return Path.GetFullPath(options.Value.RootPath);
        }

        // Auto-discover the workspace root by walking up from the app base directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "demo-repository")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the workspace root (data/demo-repository). Set ChangeSource:RootPath.");
    }

    private static string? ResolveRevision(string? requested, string? configured, string? fallback, List<string> warnings)
    {
        var candidate = requested ?? configured ?? fallback;
        if (candidate is null)
        {
            return null; // e.g. no target revision supplied → read the working tree
        }

        if (!SafeRevision.IsMatch(candidate) || candidate.StartsWith('-'))
        {
            warnings.Add($"Revision '{candidate}' is not a safe git revision; falling back to '{fallback ?? "HEAD"}'.");
            return fallback;
        }

        return candidate;
    }

    /// <summary>Resolves the git repository toplevel containing repoDir (e.g. via `git rev-parse`).</summary>
    private string? ResolveTopLevel(string repoDir)
    {
        var (exitCode, stdout, _) = RunGit(["rev-parse", "--show-toplevel"]);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var topLevel = Path.GetFullPath(stdout.Trim().Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(topLevel) ? topLevel : null;
    }

    /// <summary>True when candidate is the allowed root or a descendant of it.</summary>
    private static bool IsWithinRoot(string candidate, string root)
    {
        var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Validates a relative path; returns an error message or null when safe.</summary>
    internal static string? ValidateRelativePath(string path, string baseDir, string what)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return $"{what} is empty.";
        }

        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) && !normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return $"{what} must be relative, got '{path}'.";
        }

        var segments = normalized.Split('/');
        if (segments.Any(s => s == ".."))
        {
            return $"{what} must not contain '..', got '{path}'.";
        }

        // Reject windows-style drive letters and URI schemes (file://, http://) that
        // sneak in as "relative" paths but would resolve outside the sandbox.
        if (segments.Any(s => s.Contains(':')))
        {
            return $"{what} must be relative, got '{path}'.";
        }

        var full = Path.GetFullPath(Path.Combine(baseDir, normalized));
        var rootPrefix = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) && full != baseDir.TrimEnd(Path.DirectorySeparatorChar))
        {
            return $"{what} escapes the allowed directory, got '{path}'.";
        }

        return null;
    }

    private string? ReadTargetContent(string repoDir, string filePath, string? targetRev, string gitPath, List<string> warnings)
    {
        if (targetRev is null)
        {
            var disk = Path.Combine(repoDir, filePath);
            return File.Exists(disk) ? File.ReadAllText(disk) : null;
        }

        return GitShow(targetRev, gitPath, warnings);
    }

    private string? ReadBaseContent(string root, string baseRev, string gitPath, List<string> warnings)
        => GitShow(baseRev, gitPath, warnings);

    private string? GitShow(string revision, string gitPath, List<string> warnings)
    {
        var (exitCode, stdout, stderr) = RunGit(["show", revision + ":" + gitPath]);
        if (exitCode != 0)
        {
            warnings.Add($"git show {revision}:{gitPath} failed: {Truncate(stderr)}");
            return null;
        }

        return stdout;
    }

    private (int ExitCode, string Stdout, string Stderr) RunGit(string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ResolveRootPath()
        };
        // Fixed, validated arguments only — never user-supplied command lines.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("--no-pager");
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(options.Value.RepositoryPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return (1, "", "git could not be started.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Math.Max(1, options.Value.GitTimeoutSeconds) * 1000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best effort
                }

                return (124, "", $"git timed out after {options.Value.GitTimeoutSeconds}s.");
            }

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (Exception ex)
        {
            return (1, "", ex.Message);
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
