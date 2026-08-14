using System.Net;
using System.Text;
using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Infrastructure.Options;
using ChangeLens.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChangeLens.UnitTests.Services;

public sealed class AiServiceClientTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static AiServiceClient CreateClient(
        StubHttpMessageHandler handler,
        string? incomingCorrelationId = null,
        string? apiKey = null,
        string baseUrl = "http://ai.test")
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        var options = Options.Create(new AiOptions { BaseUrl = baseUrl, ApiKey = apiKey ?? "secret-key" });

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        if (incomingCorrelationId is not null)
        {
            accessor.HttpContext!.Request.Headers["X-Correlation-ID"] = incomingCorrelationId;
        }

        return new AiServiceClient(httpClient, options, accessor, NullLogger<AiServiceClient>.Instance);
    }

    private static AnalyzeChangeRiskRequest Request() => new()
    {
        ProjectId = Guid.NewGuid(),
        ChangeSummary = "Changed token refresh logic.",
        ChangedFiles = [new ChangedFileRequest { Path = "src/AuthClient.cs", Language = "csharp" }]
    };

    private static string ValidAiResponse() => JsonSerializer.Serialize(new
    {
        analysisType = "change-risk",
        result = new
        {
            riskLevel = "MEDIUM",
            confidence = 0.7,
            impactedComponents = (object?)null,
            riskFactors = new object[] { },
            historicalIncidents = new object[] { },
            recommendedTests = new object[] { },
            unknowns = new object[] { },
            evidence = new object[] { }
        },
        usage = new
        {
            model = "mock-gemini-3.7-flash",
            promptVersion = "risk-v1",
            latencyMs = 12,
            inputTokens = (object?)null,
            outputTokens = (object?)null,
            totalTokens = (object?)null,
            estimatedCostUsd = (object?)null,
            validationStatus = "valid",
            repairAttempts = 0,
            evidenceTruncated = false
        }
    });

    private static string ErrorResponse(int status, string code, string detail) =>
        JsonSerializer.Serialize(new
        {
            type = $"https://api.changelens.dev/errors/{code.ToLowerInvariant()}",
            title = "x",
            status,
            detail,
            code,
            traceId = "trace-1"
        });

    [Fact]
    public async Task SendsInternalContractHeaders_AndCorrelationId()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidAiResponse(), Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler, incomingCorrelationId: "trace-from-backend");
        await client.AnalyzeChangeRiskAsync(Request(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/internal/v1/analysis/risk", captured.RequestUri!.PathAndQuery);
        Assert.Equal("secret-key", captured.Headers.GetValues("X-Internal-Key").Single());
        Assert.Equal("1", captured.Headers.GetValues("X-Contract-Version").Single());
        Assert.Equal("trace-from-backend", captured.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task GeneratesCorrelationId_WhenMissing()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidAiResponse(), Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        await client.AnalyzeChangeRiskAsync(Request(), CancellationToken.None);

        var correlation = captured!.Headers.GetValues("X-Correlation-ID").Single();
        Assert.False(string.IsNullOrWhiteSpace(correlation));
        Assert.True(Guid.TryParse(correlation, out _));
    }

    [Fact]
    public async Task Success_ParsesValidatedResult()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidAiResponse(), Encoding.UTF8, "application/json")
            });

        var result = await CreateClient(handler).AnalyzeChangeRiskAsync(Request(), CancellationToken.None);

        Assert.Equal("change-risk", result.AnalysisType);
        Assert.Equal("MEDIUM", result.Result.RiskLevel);
        Assert.Equal(0.7, result.Result.Confidence);
        Assert.Equal("mock-gemini-3.7-flash", result.Usage.Model);
        Assert.Equal("valid", result.Usage.ValidationStatus);
    }

    [Theory]
    [InlineData(422, "ai_validation_failed", typeof(AiValidationFailedException))]
    [InlineData(429, "llm_rate_limited", typeof(AiRateLimitedException))]
    [InlineData(504, "ai_timeout", typeof(AiTimeoutException))]
    [InlineData(502, "ai_service_unavailable", typeof(AiUnavailableException))]
    public async Task MapsAiErrorStatuses(int status, string code, Type expected)
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(ErrorResponse(status, code, "upstream detail"), Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler);
        var ex = await Assert.ThrowsAnyAsync<ChangeLensException>(
            () => client.AnalyzeChangeRiskAsync(Request(), CancellationToken.None));

        Assert.IsType(expected, ex);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task ValidationFailure_CarriesUpstreamDetails()
    {
        var details = new { attempts = 3, errors = new[] { "risk_factors[0] references no evidence id" } };
        var envelope = JsonSerializer.Serialize(new
        {
            code = "AI_VALIDATION_FAILED",
            detail = "AI output failed validation after bounded repair.",
            status = 422,
            details
        });

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json")
            });

        var ex = await Assert.ThrowsAsync<AiValidationFailedException>(
            () => CreateClient(handler).AnalyzeChangeRiskAsync(Request(), CancellationToken.None));

        Assert.NotNull(ex.Details);
    }

    [Fact]
    public async Task NetworkFailure_MapsToUnavailable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(
            () => CreateClient(handler).AnalyzeChangeRiskAsync(Request(), CancellationToken.None));
        Assert.Contains("unreachable", ex.Message);
    }

    [Fact]
    public async Task ClientTimeout_MapsToAiTimeout()
    {
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("timeout"));
        var ex = await Assert.ThrowsAsync<AiTimeoutException>(
            () => CreateClient(handler).AnalyzeChangeRiskAsync(Request(), CancellationToken.None));
        Assert.Equal(504, ex.StatusCode);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
