using AcmePay.Domain.Exceptions;
using AcmePay.Domain.Payments;
using AcmePay.External.PaymentGateway;
using AcmePay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcmePay.Application.Payments;

public sealed record RefundPaymentCommand(decimal Amount, string Reason);

/// <summary>Refunds a succeeded payment through the gateway (partial refunds supported).</summary>
public sealed class RefundPaymentHandler(
    PaymentDbContext db,
    StripeGatewayClient gateway,
    ILogger<RefundPaymentHandler> logger)
{
    public async Task<Refund> HandleAsync(
        Guid paymentId, RefundPaymentCommand command, CancellationToken ct)
    {
        var payment = await db.Payments.FindAsync([paymentId], ct)
            ?? throw new KeyNotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Succeeded)
        {
            throw new InvalidOperationException(
                $"Cannot refund a payment in state {payment.Status}.");
        }

        var refund = payment.CreateRefund(command.Amount, command.Reason);
        try
        {
            var gatewayRefund = await gateway.RefundAsync(
                payment.GatewayTransactionId!, command.Amount, command.Reason, ct);

            refund.MarkCompleted(gatewayRefund.RefundId);
            db.Refunds.Add(refund);
            await db.SaveChangesAsync(ct);
            return refund;
        }
        catch (PaymentGatewayException ex)
        {
            // The refund intent is kept in a Pending state; a background job retries it.
            logger.LogWarning(ex, "Refund for {PaymentId} pending gateway retry", paymentId);
            refund.MarkPending();
            db.Refunds.Add(refund);
            await db.SaveChangesAsync(ct);
            return refund;
        }
    }
}
