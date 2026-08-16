using ChangeLens.Application.Dtos;
using ChangeLens.Infrastructure.Analysis;
using ChangeLens.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChangeLens.UnitTests.Analysis;

/// <summary>
/// Security and correctness tests for the local git change source (brief §12–13):
/// repository/file path validation, git revision validation, and real base→target
/// resolution against the demo repository. git is only ever invoked with a fixed,
/// validated argument list — never a user-supplied command line.
/// </summary>
public sealed class GitChangeSourceTests
{
    private static readonly string DemoRepo = DemoRepoLocator.FindRoot();
    private static readonly string WorkspaceRoot = Directory.GetParent(DemoRepo)!.Parent!.FullName;

    private static GitChangeSource Source(string repoPath = "data/demo-repository")
        => new(
            Options.Create(new ChangeSourceOptions
            {
                RootPath = WorkspaceRoot,
                RepositoryPath = repoPath
            }),
            NullLogger<GitChangeSource>.Instance);

    private static ChangedFileRequest File(string path, string changeType = "modified")
        => new() { Path = path, ChangeType = changeType, Language = "csharp" };

    [Fact]
    public void PathTraversal_IsRejected_AndNoGitRuns()
    {
        var resolution = Source().ResolveChange(
            [File("../outside.cs"), File("src/../../secret.cs")],
            baseRevision: null,
            targetRevision: null);

        Assert.Empty(resolution.Files);
        Assert.Contains(resolution.Warnings, w => w.Contains(".."));
    }

    [Fact]
    public void AbsoluteAndDriveLetterPaths_AreRejected()
    {
        var resolution = Source().ResolveChange(
            [File("/etc/passwd"), File("C:\\Windows\\system32\\evil.cs"), File("file:///etc/passwd")],
            baseRevision: null,
            targetRevision: null);

        Assert.Empty(resolution.Files);
        Assert.Contains(resolution.Warnings, w => w.Contains("must be relative"));
    }

    [Fact]
    public void EmptyPath_IsRejected()
    {
        var resolution = Source().ResolveChange([File("")], baseRevision: null, targetRevision: null);

        Assert.Empty(resolution.Files);
        Assert.Contains(resolution.Warnings, w => w.Contains("empty"));
    }

    [Fact]
    public void UnsafeRevisions_FallBackToHead_WithWarning()
    {
        var resolution = Source().ResolveChange(
            [File("src/AcmePay.Application/Auth/TokenService.cs")],
            baseRevision: "-h",
            targetRevision: "abc..def");

        // Both fall back — the change is still resolved against HEAD, never executed.
        Assert.Single(resolution.Files);
        Assert.Contains(resolution.Warnings, w => w.Contains("not a safe git revision"));
        Assert.Equal("HEAD", resolution.BaseRevision);
    }

    [Fact]
    public void ValidRevisions_AreAcceptedWithoutWarning()
    {
        var resolution = Source().ResolveChange(
            [File("src/AcmePay.Application/Auth/TokenService.cs")],
            baseRevision: "HEAD~1",
            targetRevision: "HEAD");

        Assert.DoesNotContain(resolution.Warnings, w => w.Contains("not a safe git revision"));
        Assert.Equal("HEAD~1", resolution.BaseRevision);
        Assert.Equal("HEAD", resolution.TargetRevision);
    }

    [Fact]
    public void WorkingTreeChange_ResolvesBaseFromGit_AndTargetFromDisk()
    {
        // The demo repository's TokenService.cs carries the committed demo follow-up
        // change (signing-key parsing extraction + rotation fingerprint). The base is
        // the parent of the last commit that modified the file (git), the target is the
        // current working tree — so the resolution must come from two different sources
        // on any clean checkout.
        var resolution = Source().ResolveChange(
            [File("src/AcmePay.Application/Auth/TokenService.cs")],
            baseRevision: DemoChangeLocator.BaseRevision(),
            targetRevision: null);

        var file = Assert.Single(resolution.Files);
        Assert.NotNull(file.BaseContent);
        Assert.NotNull(file.TargetContent);
        Assert.NotEqual(file.BaseContent, file.TargetContent);
        Assert.Contains("CurrentSigningKeyFingerprint", file.TargetContent!);
        Assert.Contains("ParseSigningKeys", file.TargetContent!);
        Assert.DoesNotContain("CurrentSigningKeyFingerprint", file.BaseContent!);
    }

    [Fact]
    public void TargetRevisionHead_IgnoresWorkingTree()
    {
        var resolution = Source().ResolveChange(
            [File("src/AcmePay.Application/Auth/TokenService.cs")],
            baseRevision: "HEAD",
            targetRevision: "HEAD");

        var file = Assert.Single(resolution.Files);
        Assert.NotNull(file.TargetContent);
        Assert.Equal(file.BaseContent, file.TargetContent);
    }

    [Fact]
    public void MissingFile_IsToleratedWithWarning_NotAnException()
    {
        var resolution = Source().ResolveChange(
            [File("src/DoesNotExist.cs")],
            baseRevision: "HEAD",
            targetRevision: null);

        var file = Assert.Single(resolution.Files);
        Assert.Null(file.BaseContent);
        Assert.Null(file.TargetContent);
        Assert.NotEmpty(resolution.Warnings);
    }

    [Fact]
    public void RepositoryPathOutsideRoot_IsRejected()
    {
        // A repository path escaping the allowed root is configuration, not user input,
        // but must fail loudly instead of reading outside the sandbox.
        var source = new GitChangeSource(
            Options.Create(new ChangeSourceOptions
            {
                RootPath = Path.GetTempPath(),
                RepositoryPath = "../outside-repo"
            }),
            NullLogger<GitChangeSource>.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => source.ResolveChange(
            [File("src/x.cs")], baseRevision: null, targetRevision: null));

        Assert.Contains("RepositoryPath", ex.Message);
    }
}
