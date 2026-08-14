namespace AcmePay.Domain.Payments;

public sealed class Payment
{
    private Payment() { } // EF

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid CustomerId { get; private set; }

    public Guid MerchantId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string PaymentMethodId { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public string? GatewayTransactionId { get; private set; }

    public string? DeclineReason { get; private set; }

    public DateTime ProcessedAtUtc { get; private set; }

    public static Payment Create(
        Guid customerId, Guid merchantId, decimal amount, string currency, string methodId)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        return new Payment
        {
            CustomerId = customerId,
            MerchantId = merchantId,
            Amount = amount,
            Currency = currency,
            PaymentMethodId = methodId,
            ProcessedAtUtc = DateTime.UtcNow
        };
    }

    public void MarkCharged(string gatewayTransactionId)
    {
        Status = PaymentStatus.Succeeded;
        GatewayTransactionId = gatewayTransactionId;
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Declined;
        DeclineReason = reason;
    }

    public Refund CreateRefund(decimal amount, string reason)
    {
        if (amount > Amount)
        {
            throw new InvalidOperationException("Refund amount exceeds the payment amount.");
        }

        return Refund.Create(Id, amount, reason);
    }
}
