using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Domain.Projects;
using ChangeLens.UnitTests.Infrastructure;

namespace ChangeLens.UnitTests.Services;

public sealed class RepositoryServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Register_ValidRepository_ReturnsRegisteredRepository()
    {
        var projectId = await CreateProjectAsync();

        var result = await Repositories.RegisterAsync(projectId, new CreateRepositoryRequest
        {
            Name = "auth-api",
            Url = "https://github.com/demo/auth-api.git",
            Language = "csharp"
        }, CancellationToken.None);

        Assert.Equal(projectId, result.ProjectId);
        Assert.Equal("auth-api", result.Name);
    }

    [Fact]
    public async Task Register_InvalidUrl_ThrowsValidation()
    {
        var projectId = await CreateProjectAsync();

        await Assert.ThrowsAsync<ValidationException>(async () =>
            await Repositories.RegisterAsync(projectId, new CreateRepositoryRequest
            {
                Name = "auth-api",
                Url = "not a url",
                Language = "csharp"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Register_AsViewer_ThrowsForbidden()
    {
        var projectId = await CreateProjectAsync();

        var viewer = FakeCurrentUser.Standard();
        await Projects.AddMemberAsync(projectId, viewer.UserId, "viewer@test.dev", "Viewer", ProjectRole.Viewer, CancellationToken.None);

        User = viewer;
        await Assert.ThrowsAsync<ForbiddenAccessException>(async () =>
            await Repositories.RegisterAsync(projectId, new CreateRepositoryRequest
            {
                Name = "auth-api",
                Url = "https://github.com/demo/auth-api.git",
                Language = "csharp"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Get_NonMember_ThrowsNotFound()
    {
        var projectId = await CreateProjectAsync();
        var repo = await Repositories.RegisterAsync(projectId, new CreateRepositoryRequest
        {
            Name = "auth-api",
            Url = "https://github.com/demo/auth-api.git",
            Language = "csharp"
        }, CancellationToken.None);

        User = FakeCurrentUser.Standard();
        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await Repositories.GetAsync(repo.Id, CancellationToken.None));
    }
}
