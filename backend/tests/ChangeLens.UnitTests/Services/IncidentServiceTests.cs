using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Domain.Incidents;
using ChangeLens.UnitTests.Infrastructure;

namespace ChangeLens.UnitTests.Services;

public sealed class IncidentServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Create_WithEvents_PersistsTimeline()
    {
        var projectId = await CreateProjectAsync();

        var incident = await Incidents.CreateAsync(new CreateIncidentRequest
        {
            ProjectId = projectId,
            Title = "Token refresh failures",
            Severity = IncidentSeverity.Sev2,
            Events =
            [
                new CreateIncidentEventRequest
                {
                    Type = IncidentEventType.Log,
                    Source = "auth-api",
                    Message = "invalid signature"
                },
                new CreateIncidentEventRequest
                {
                    Type = IncidentEventType.Deployment,
                    Source = "ci",
                    Message = "deployed v2.4.1"
                }
            ]
        }, CancellationToken.None);

        Assert.Equal(IncidentSeverity.Sev2, incident.Severity);
        Assert.Equal(2, incident.Events.Count);
        Assert.All(incident.Events, e => Assert.NotEqual(Guid.Empty, e.Id));
    }

    [Fact]
    public async Task Create_StartedAtDefaultsToUtcNow()
    {
        var projectId = await CreateProjectAsync();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var incident = await Incidents.CreateAsync(new CreateIncidentRequest
        {
            ProjectId = projectId,
            Title = "Defaults"
        }, CancellationToken.None);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(incident.StartedAtUtc, before, after);
    }

    [Fact]
    public async Task Create_AffectedServiceFromOtherProject_ThrowsValidation()
    {
        var projectId = await CreateProjectAsync();
        var otherProjectId = await CreateProjectAsync("Other Project");
        var service = await Services.CreateAsync(otherProjectId, new CreateServiceRequest { Name = "other-api" }, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(async () =>
            await Incidents.CreateAsync(new CreateIncidentRequest
            {
                ProjectId = projectId,
                Title = "Bad service ref",
                AffectedServiceId = service.Id
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Create_NonMember_ThrowsNotFound()
    {
        var projectId = await CreateProjectAsync();

        User = FakeCurrentUser.Standard();
        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await Incidents.CreateAsync(new CreateIncidentRequest
            {
                ProjectId = projectId,
                Title = "Sneaky incident"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task List_FiltersByStatusAndSeverity()
    {
        var projectId = await CreateProjectAsync();

        await Incidents.CreateAsync(new CreateIncidentRequest { ProjectId = projectId, Title = "Open one", Severity = IncidentSeverity.Sev1 }, CancellationToken.None);
        await Incidents.CreateAsync(new CreateIncidentRequest { ProjectId = projectId, Title = "Resolved one", Severity = IncidentSeverity.Sev1, Status = IncidentStatus.Resolved }, CancellationToken.None);
        await Incidents.CreateAsync(new CreateIncidentRequest { ProjectId = projectId, Title = "Open sev3", Severity = IncidentSeverity.Sev3 }, CancellationToken.None);

        var open = await Incidents.ListAsync(projectId, IncidentStatus.Open, null, null, 1, 20, CancellationToken.None);
        var sev1 = await Incidents.ListAsync(projectId, null, IncidentSeverity.Sev1, null, 1, 20, CancellationToken.None);

        Assert.Equal(2, open.Total);
        Assert.Equal(2, sev1.Total);
    }

    [Fact]
    public async Task Update_ChangesStatusAndAudits()
    {
        var projectId = await CreateProjectAsync();
        var created = await Incidents.CreateAsync(new CreateIncidentRequest
        {
            ProjectId = projectId,
            Title = "Something broke",
            Status = IncidentStatus.Open
        }, CancellationToken.None);

        var updated = await Incidents.UpdateAsync(created.Id, new UpdateIncidentRequest
        {
            Status = IncidentStatus.Resolved,
            Summary = "Root cause found"
        }, CancellationToken.None);

        Assert.Equal(IncidentStatus.Resolved, updated.Status);
        Assert.Equal("Root cause found", updated.Summary);
    }

    [Fact]
    public async Task AddEvent_AppendsToTimeline()
    {
        var projectId = await CreateProjectAsync();
        var created = await Incidents.CreateAsync(new CreateIncidentRequest
        {
            ProjectId = projectId,
            Title = "Something broke"
        }, CancellationToken.None);

        await Incidents.AddEventAsync(created.Id, new CreateIncidentEventRequest
        {
            Type = IncidentEventType.Error,
            Message = "Exception: timeout"
        }, CancellationToken.None);

        var detail = await Incidents.GetAsync(created.Id, CancellationToken.None);

        Assert.Single(detail.Events);
        Assert.Equal("Exception: timeout", detail.Events[0].Message);
    }
}
