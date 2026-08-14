namespace AcmePay.Domain.Payments;

public sealed class Refund
{
    private Refund() { } // EF

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid PaymentId { get; private set; }

    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public RefundStatus Status { get; private set; } = RefundStatus.Pending;

    public string? GatewayRefundId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static Refund Create(Guid paymentId, decimal amount, string reason)
        => new()
        {
            PaymentId = paymentId,
            Amount = amount,
            Reason = reason,
            Status = RefundStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

    public void MarkCompleted(string gatewayRefundId)
    {
        Status = RefundStatus.Completed;
        GatewayRefundId = gatewayRefundId;
    }

    public void MarkPending() => Status = RefundStatus.Pending;

    public enum RefundStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2
    }
}
