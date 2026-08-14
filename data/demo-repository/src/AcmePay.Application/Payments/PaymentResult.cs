using AcmePay.Domain.Payments;

namespace AcmePay.Application.Payments;

public sealed record PaymentResult(
    Guid PaymentId,
    PaymentStatus Status,
    string? GatewayTransactionId,
    string? DeclineReason,
    DateTime ProcessedAtUtc);
