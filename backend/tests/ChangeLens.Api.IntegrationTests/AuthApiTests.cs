using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;

namespace ChangeLens.Api.IntegrationTests;

[Collection("database")]
public sealed class AuthApiTests
{
    private readonly TestApi _api;

    public AuthApiTests(DatabaseFixture fixture) => _api = new TestApi(fixture.Factory);

    [Fact]
    public async Task Register_Returns201_WithBearerTokenAndEngineerRole()
    {
        using var client = _api.NewClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"reg-{Guid.NewGuid():N}@test.dev",
            password = "Passw0rd!Test",
            displayName = "Reg User"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.Equal("Bearer", auth.TokenType);
        Assert.Contains("Engineer", auth.User.Roles);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = $"dup-{Guid.NewGuid():N}@test.dev";
        await _api.RegisterAsync(email);

        using var client = _api.NewClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "Passw0rd!Test"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        using var client = _api.NewClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "not-an-email",
            password = "Passw0rd!Test"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200()
    {
        var email = $"login-{Guid.NewGuid():N}@test.dev";
        await _api.RegisterAsync(email);

        using var client = _api.NewClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Passw0rd!Test"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var email = $"login-{Guid.NewGuid():N}@test.dev";
        await _api.RegisterAsync(email);

        using var client = _api.NewClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "WrongPassword!1"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsUserWithMemberships()
    {
        var (token, userId) = await _api.RegisterAsync($"me-{Guid.NewGuid():N}@test.dev");
        await _api.CreateProjectAsync(token, "Membership Project");

        using var client = _api.NewClient(token);
        var response = await client.GetAsync("/api/v1/auth/me");
        response.EnsureSuccessStatusCode();

        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);
        Assert.Equal(userId, me.User.Id);
        Assert.Single(me.Memberships);
        Assert.Equal("Membership Project", me.Memberships[0].ProjectName);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        using var client = _api.NewClient();
        var response = await client.GetAsync("/api/v1/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
