using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AcmePay.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcmePay.External.PaymentGateway;

/// <summary>
/// Client for the third-party payment gateway ("Stripe-like").
///
/// Synthetic demo integration: exercises retry/timeout logic against an external
/// API with a JSON contract. Retries only transient failures (429/5xx, timeouts,
/// transport errors) with bounded backoff; never retries 4xx client errors.
/// </summary>
public sealed class StripeGatewayClient
{
    private readonly HttpClient _http;
    private readonly PaymentGatewayOptions _options;
    private readonly ILogger<StripeGatewayClient> _logger;

    public StripeGatewayClient(
        HttpClient http,
        IOptions<PaymentGatewayOptions> options,
        ILogger<StripeGatewayClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        http.BaseAddress = new Uri(_options.BaseUrl);
        http.Timeout = _options.Timeout;
    }

    public async Task<GatewayChargeResponse> AuthorizeAsync(
        decimal amount,
        string currency,
        string paymentMethodId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = new GatewayChargeRequest(
            AmountCents: (long)(amount * 100),
            Currency: currency,
            PaymentMethodId: paymentMethodId,
            IdempotencyKey: idempotencyKey ?? Guid.NewGuid().ToString());

        return await ExecuteWithRetryAsync(
            async ct =>
            {
                using var response = await _http.PostAsJsonAsync(
                    "v1/charges", request, ct);

                if (response.IsSuccessStatusCode)
                {
                    return (await response.Content
                        .ReadFromJsonAsync<GatewayChargeResponse>(ct))!;
                }

                if (IsTransient(response.StatusCode))
                {
                    throw new PaymentGatewayException(
                        $"Gateway returned {(int)response.StatusCode}",
                        (int)response.StatusCode);
                }

                throw new PaymentGatewayException(
                    $"Gateway declined charge with status {(int)response.StatusCode}",
                    (int)response.StatusCode);
            },
            cancellationToken);
    }

    public async Task<GatewayRefundResponse> RefundAsync(
        string gatewayChargeId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken)
    {
        var request = new GatewayRefundRequest(
            ChargeId: gatewayChargeId,
            AmountCents: (long)(amount * 100),
            Reason: reason);

        return await ExecuteWithRetryAsync(
            async ct =>
            {
                using var response = await _http.PostAsJsonAsync(
                    "v1/refunds", request, ct);

                if (response.IsSuccessStatusCode)
                {
                    return (await response.Content
                        .ReadFromJsonAsync<GatewayRefundResponse>(ct))!;
                }

                throw new PaymentGatewayException(
                    $"Gateway refund failed with status {(int)response.StatusCode}",
                    (int)response.StatusCode);
            },
            cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await operation(cancellationToken);
            }
            catch (PaymentGatewayException ex) when (ex.IsRetryable && attempt <= _options.MaxRetries)
            {
                _logger.LogWarning(ex,
                    "Gateway transient failure (attempt {Attempt}/{Max}); retrying",
                    attempt, _options.MaxRetries);
                await Task.Delay(_options.BaseBackoff * attempt, cancellationToken);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                throw new PaymentGatewayException("Gateway request timed out", 504);
            }
            catch (HttpRequestException ex)
            {
                if (attempt <= _options.MaxRetries)
                {
                    _logger.LogWarning(ex,
                        "Gateway unreachable (attempt {Attempt}/{Max}); retrying",
                        attempt, _options.MaxRetries);
                    await Task.Delay(_options.BaseBackoff * attempt, cancellationToken);
                    continue;
                }

                throw new PaymentGatewayException("Gateway unreachable", 502, ex);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}

public sealed record GatewayChargeRequest(
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("payment_method_id")] string PaymentMethodId,
    [property: JsonPropertyName("idempotency_key")] string IdempotencyKey);

public sealed record GatewayChargeResponse(
    [property: JsonPropertyName("charge_id")] string ChargeId,
    [property: JsonPropertyName("status")] string Status)
{
    public string TransactionId => ChargeId;
}

public sealed record GatewayRefundRequest(
    [property: JsonPropertyName("charge_id")] string ChargeId,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record GatewayRefundResponse(
    [property: JsonPropertyName("refund_id")] string RefundId,
    [property: JsonPropertyName("status")] string Status);
