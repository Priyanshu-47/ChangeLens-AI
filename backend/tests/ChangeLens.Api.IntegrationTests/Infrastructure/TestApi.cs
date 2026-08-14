using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChangeLens.Application.Dtos;

namespace ChangeLens.Api.IntegrationTests.Infrastructure;

/// <summary>HTTP helpers for driving the API under test.</summary>
public sealed class TestApi(AppFactory factory)
{
    public const string SeedPassword = "ViewerPass!2026";

    public HttpClient NewClient(string? token = null)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>Registers a brand-new user (Engineer role) and returns (token, userId).</summary>
    public async Task<(string Token, Guid UserId)> RegisterAsync(string email)
    {
        using var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "Passw0rd!Test",
            displayName = email.Split('@')[0]
        });

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>()
            ?? throw new InvalidOperationException("Register response was empty.");

        return (auth.AccessToken, auth.User.Id);
    }

    public async Task<string> LoginAsync(string email, string password)
    {
        using var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>()
            ?? throw new InvalidOperationException("Login response was empty.");

        return auth.AccessToken;
    }

    public Task<string> LoginAsSeededAsync(string email) => LoginAsync(email, SeedPassword);

    public async Task<Guid> CreateProjectAsync(string token, string name = "Integration Project")
    {
        using var client = NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name });
        response.EnsureSuccessStatusCode();

        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>()
            ?? throw new InvalidOperationException("Project response was empty.");

        return project.Id;
    }
}
