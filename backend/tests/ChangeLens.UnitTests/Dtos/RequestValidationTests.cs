using System.ComponentModel.DataAnnotations;
using ChangeLens.Application.Dtos;
using ChangeLens.Domain.Incidents;
using ChangeLens.Domain.Projects;

namespace ChangeLens.UnitTests.Dtos;

public sealed class RequestValidationTests
{
    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void CreateProjectRequest_Valid_NoErrors()
    {
        Assert.Empty(Validate(new CreateProjectRequest { Name = "Demo" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateProjectRequest_MissingName_Invalid(string? name)
    {
        Assert.NotEmpty(Validate(new CreateProjectRequest { Name = name ?? string.Empty }));
    }

    [Fact]
    public void CreateProjectRequest_OverlongName_Invalid()
    {
        Assert.NotEmpty(Validate(new CreateProjectRequest { Name = new string('x', 121) }));
    }

    [Fact]
    public void CreateRepositoryRequest_Valid_NoErrors()
    {
        Assert.Empty(Validate(new CreateRepositoryRequest
        {
            Name = "auth-api",
            Url = "https://github.com/org/auth-api.git",
            Language = "csharp"
        }));
    }

    [Fact]
    public void CreateRepositoryRequest_MissingLanguage_Invalid()
    {
        Assert.NotEmpty(Validate(new CreateRepositoryRequest
        {
            Name = "auth-api",
            Url = "https://github.com/org/auth-api.git"
        }));
    }

    [Fact]
    public void AddMemberRequest_InvalidRole_Invalid()
    {
        var request = new AddMemberRequest { Email = "a@b.dev", Role = (ProjectRole)999 };
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void RegisterRequest_ShortPassword_Invalid()
    {
        var request = new RegisterRequest { Email = "a@b.dev", Password = "short" };
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void IncidentEventRequest_InvalidType_Invalid()
    {
        // Note: DataAnnotations validation does not recurse into collection items, so
        // the event DTO is validated directly here; the API layer additionally rejects
        // unknown enum values at JSON deserialization (covered by integration tests).
        var request = new CreateIncidentEventRequest { Type = (IncidentEventType)99 };

        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void LoginRequest_InvalidEmail_Invalid()
    {
        Assert.NotEmpty(Validate(new LoginRequest { Email = "not-an-email", Password = "Password1" }));
    }
}
