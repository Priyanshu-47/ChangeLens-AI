namespace AcmePay.Application.Payments;

public sealed record ProcessPaymentCommand(
    Guid CustomerId,
    Guid MerchantId,
    decimal Amount,
    string Currency,
    string PaymentMethodId,
    string? IdempotencyKey = null);
