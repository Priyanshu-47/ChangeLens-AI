using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AcmePay.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcmePay.External.PaymentGateway;

/// <summary>
/// Client for the payouts API. Demonstrates a second external integration with
/// a different contract shape and a stricter timeout.
/// </summary>
public sealed class PayoutGatewayClient(
    HttpClient http,
    IOptions<PaymentGatewayOptions> options,
    ILogger<PayoutGatewayClient> logger)
{
    public async Task<PayoutResponse> CreatePayoutAsync(
        PayoutRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "v1/payouts", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (await response.Content
                    .ReadFromJsonAsync<PayoutResponse>(cancellationToken))!;
            }

            if ((int)response.StatusCode is 429 or >= 500)
            {
                throw new PaymentGatewayException(
                    $"Payout gateway returned {(int)response.StatusCode}",
                    (int)response.StatusCode);
            }

            throw new PaymentGatewayException(
                $"Payout rejected with status {(int)response.StatusCode}",
                (int)response.StatusCode);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new PaymentGatewayException("Payout request timed out", 504);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("Payout gateway unreachable", 502, ex);
        }
    }
}

public sealed record PayoutRequest(
    [property: JsonPropertyName("merchant_id")] string MerchantId,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("currency")] string Currency);

public sealed record PayoutResponse(
    [property: JsonPropertyName("payout_id")] string PayoutId,
    [property: JsonPropertyName("status")] string Status);
