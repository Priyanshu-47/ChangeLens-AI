using System.Net.Http.Json;
using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChangeLens.Infrastructure.Services;

/// <summary>
/// Typed client for the internal AI service (POST /internal/v1/analysis/risk).
/// Sends the shared internal key and the correlation id, enforces the HTTP timeout,
/// and maps AI-service error envelopes to typed exceptions. Gemini is never called
/// from .NET — the dependency is always .NET → FastAPI → Gemini (ADR-0002).
/// </summary>
public sealed class AiServiceClient(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AiServiceClient> logger) : IAiServiceClient
{
    // The AI service contract is camelCase (docs/ai-service-boundary.md §3): serialize with
    // the camelCase policy; accept case-insensitive input on the way back in.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ChangeRiskAnalysisResponse> AnalyzeChangeRiskAsync(
        AnalyzeChangeRiskRequest request, CancellationToken ct)
    {
        var correlationId = ResolveCorrelationId();

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/v1/analysis/risk")
        {
            Content = JsonContent.Create(request, options: Json)
        };
        httpRequest.Headers.Add("X-Internal-Key", options.Value.ApiKey);
        httpRequest.Headers.Add("X-Contract-Version", AiOptions.ContractVersion);
        httpRequest.Headers.Add("X-Correlation-ID", correlationId);

        logger.LogInformation(
            "AI analysis request started for project {ProjectId} (correlation {CorrelationId})",
            request.ProjectId, correlationId);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<ChangeRiskAnalysisResponse>(body, Json)
                    ?? throw new AiUnavailableException("AI service returned an empty response body.");
            }

            throw MapError(response, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AI analysis request timed out after {Timeout}s (correlation {CorrelationId})",
                options.Value.TimeoutSeconds, correlationId);
            throw new AiTimeoutException(
                $"AI service did not respond within {options.Value.TimeoutSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "AI service unreachable (correlation {CorrelationId})", correlationId);
            throw new AiUnavailableException("AI service is unreachable.");
        }
    }

    public async Task<IncidentAnalysisResponseDto> AnalyzeIncidentAsync(
        IncidentAnalysisRequestDto request, CancellationToken ct)
    {
        var correlationId = ResolveCorrelationId();

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/v1/analysis/incident")
        {
            Content = JsonContent.Create(request, options: Json)
        };
        httpRequest.Headers.Add("X-Internal-Key", options.Value.ApiKey);
        httpRequest.Headers.Add("X-Contract-Version", AiOptions.ContractVersion);
        httpRequest.Headers.Add("X-Correlation-ID", correlationId);

        logger.LogInformation(
            "Incident analysis request started for project {ProjectId} (correlation {CorrelationId})",
            request.ProjectId, correlationId);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<IncidentAnalysisResponseDto>(body, Json)
                    ?? throw new AiUnavailableException("AI service returned an empty response body.");
            }

            throw MapError(response, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Incident analysis request timed out after {Timeout}s (correlation {CorrelationId})",
                options.Value.TimeoutSeconds, correlationId);
            throw new AiTimeoutException(
                $"AI service did not respond within {options.Value.TimeoutSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "AI service unreachable (correlation {CorrelationId})", correlationId);
            throw new AiUnavailableException("AI service is unreachable.");
        }
    }

    private string ResolveCorrelationId()
    {
        var incoming = httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].ToString();
        return string.IsNullOrWhiteSpace(incoming) ? Guid.NewGuid().ToString("N") : incoming;
    }

    private static ChangeLensException MapError(HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        AiErrorEnvelope? envelope = null;

        try
        {
            envelope = JsonSerializer.Deserialize<AiErrorEnvelope>(body, Json);
        }
        catch (JsonException)
        {
            // Non-envelope upstream body — fall through to the generic mapping.
        }

        var detail = envelope?.Detail ?? $"AI service error (HTTP {status}).";

        return status switch
        {
            429 => new AiRateLimitedException(detail),
            504 => new AiTimeoutException(detail),
            422 => new AiValidationFailedException(detail, envelope?.Details),
            _ => new AiUnavailableException(detail)
        };
    }

    private sealed class AiErrorEnvelope
    {
        public string? Code { get; init; }
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public object? Details { get; init; }
    }
}
