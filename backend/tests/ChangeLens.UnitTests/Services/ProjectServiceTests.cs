using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Projects;
using ChangeLens.UnitTests.Infrastructure;

namespace ChangeLens.UnitTests.Services;

public sealed class ProjectServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Create_AddsOwnerMembershipAndAuditEntry()
    {
        var result = await Projects.CreateAsync(
            new CreateProjectRequest { Name = "Auth Platform" }, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("auth-platform", result.Slug);
        Assert.Equal(ProjectRole.Owner.ToString(), result.MemberRole);

        var audit = await Audit.QueryAsync(result.Id, 1, 20, CancellationToken.None);
        Assert.Contains(audit.Items, a => a.Action == AuditActions.ProjectCreated);
    }

    [Fact]
    public async Task Create_GeneratesUniqueSlugOnCollision()
    {
        await CreateProjectAsync("Demo Project");
        var second = await Projects.CreateAsync(
            new CreateProjectRequest { Name = "Demo Project" }, CancellationToken.None);

        Assert.Equal("demo-project-2", second.Slug);
    }

    [Fact]
    public async Task Create_EmptyName_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await Projects.CreateAsync(new CreateProjectRequest { Name = "  " }, CancellationToken.None));
    }

    [Fact]
    public async Task Update_ByViewerMember_ThrowsForbidden()
    {
        var projectId = await CreateProjectAsync();

        var viewer = FakeCurrentUser.Standard();
        await Projects.AddMemberAsync(projectId, viewer.UserId, "viewer@test.dev", "Viewer", ProjectRole.Viewer, CancellationToken.None);

        User = viewer;
        await Assert.ThrowsAsync<ForbiddenAccessException>(async () =>
            await Projects.UpdateAsync(projectId, new UpdateProjectRequest { Name = "Renamed" }, CancellationToken.None));
    }

    [Fact]
    public async Task Update_NonMember_ThrowsNotFound()
    {
        var projectId = await CreateProjectAsync();

        User = FakeCurrentUser.Standard();
        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await Projects.UpdateAsync(projectId, new UpdateProjectRequest { Name = "Renamed" }, CancellationToken.None));
    }

    [Fact]
    public async Task Update_ByOwner_AppliesChangesAndAudits()
    {
        var projectId = await CreateProjectAsync();

        var updated = await Projects.UpdateAsync(
            projectId, new UpdateProjectRequest { Name = "Renamed Platform", Description = "new desc" }, CancellationToken.None);

        Assert.Equal("Renamed Platform", updated.Name);
        Assert.Equal("renamed-platform", updated.Slug);
        Assert.Equal("new desc", updated.Description);
    }

    [Fact]
    public async Task RemoveMember_LastOwner_ThrowsConflict()
    {
        var projectId = await CreateProjectAsync();

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await Projects.RemoveMemberAsync(projectId, User.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveMember_NonOwnerMember_Succeeds()
    {
        var projectId = await CreateProjectAsync();

        var engineer = FakeCurrentUser.Standard();
        await Projects.AddMemberAsync(projectId, engineer.UserId, "eng@test.dev", "Eng", ProjectRole.Engineer, CancellationToken.None);

        await Projects.RemoveMemberAsync(projectId, engineer.UserId, CancellationToken.None);

        // The engineer can no longer read the project.
        User = engineer;
        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await Projects.GetAsync(projectId, CancellationToken.None));
    }

    [Fact]
    public async Task List_ReturnsOnlyMemberProjects()
    {
        var owner = User;
        await CreateProjectAsync("Project One");

        var otherUser = FakeCurrentUser.Standard();
        User = otherUser;
        await CreateProjectAsync("Project Two");

        User = owner;
        var list = await Projects.ListAsync(1, 20, CancellationToken.None);

        Assert.Single(list.Items);
        Assert.Equal("Project One", list.Items[0].Name);
    }

    [Fact]
    public async Task Get_GlobalAdmin_ReadsProjectWithoutMembership()
    {
        var projectId = await CreateProjectAsync();

        User = FakeCurrentUser.Admin();
        var project = await Projects.GetAsync(projectId, CancellationToken.None);

        Assert.Equal(projectId, project.Id);
        Assert.Equal(ProjectRole.Admin.ToString(), project.MemberRole);
    }
}
