namespace AcmePay.External.PaymentGateway;

/// <summary>
/// Options for the third-party payment gateway integration.
/// </summary>
public sealed class PaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";

    public string BaseUrl { get; set; } = "https://gateway.example.com";

    public string ApiKey { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public int MaxRetries { get; set; } = 3;

    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromMilliseconds(200);
}
