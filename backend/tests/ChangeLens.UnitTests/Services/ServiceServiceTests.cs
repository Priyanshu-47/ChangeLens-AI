using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.UnitTests.Infrastructure;

namespace ChangeLens.UnitTests.Services;

public sealed class ServiceServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Create_ValidService_ReturnsService()
    {
        var projectId = await CreateProjectAsync();

        var result = await Services.CreateAsync(projectId, new CreateServiceRequest
        {
            Name = "auth-api",
            Language = "csharp"
        }, CancellationToken.None);

        Assert.Equal("auth-api", result.Name);
        Assert.Equal("csharp", result.Language);
    }

    [Fact]
    public async Task Create_EmptyName_ThrowsValidation()
    {
        var projectId = await CreateProjectAsync();

        await Assert.ThrowsAsync<ValidationException>(async () =>
            await Services.CreateAsync(projectId, new CreateServiceRequest { Name = " " }, CancellationToken.None));
    }

    [Fact]
    public async Task List_NonMember_ThrowsNotFound()
    {
        var projectId = await CreateProjectAsync();

        User = FakeCurrentUser.Standard();
        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await Services.ListAsync(projectId, CancellationToken.None));
    }

    [Fact]
    public async Task Get_Service_ReturnsServiceForMember()
    {
        var projectId = await CreateProjectAsync();
        var created = await Services.CreateAsync(projectId, new CreateServiceRequest { Name = "billing-api" }, CancellationToken.None);

        var fetched = await Services.GetAsync(created.Id, CancellationToken.None);

        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("billing-api", fetched.Name);
    }
}
