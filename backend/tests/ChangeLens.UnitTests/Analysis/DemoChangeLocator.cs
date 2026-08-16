using System.Diagnostics;

namespace ChangeLens.UnitTests.Analysis;

/// <summary>
/// Locates the committed demo follow-up change in the demo repository. The change
/// (signing-key parsing extraction + rotation fingerprint in TokenService.cs) is
/// committed, so the pre-change baseline is the parent of the most recent commit
/// that modified the file — robust to intervening commits (docs, CI, etc.).
/// </summary>
internal static class DemoChangeLocator
{
    private const string TokenServicePath = "data/demo-repository/src/AcmePay.Application/Auth/TokenService.cs";

    public static string WorkspaceRoot =>
        Directory.GetParent(DemoRepoLocator.FindRoot())!.Parent!.FullName;

    /// <summary>Parent of the most recent commit that modified TokenService.cs.</summary>
    public static string BaseRevision()
    {
        var lastCommit = Git($"log -1 --format=%H -- {TokenServicePath}");
        return Git($"rev-parse {lastCommit}^");
    }

    private static string Git(string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = WorkspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }
}
