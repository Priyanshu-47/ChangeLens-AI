using AcmePay.Domain.Exceptions;
using AcmePay.Domain.Payments;
using AcmePay.External.PaymentGateway;
using AcmePay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcmePay.Application.Payments;

/// <summary>Orchestrates a payment: authorize via the gateway, then persist.</summary>
public sealed class ProcessPaymentHandler(
    PaymentDbContext db,
    StripeGatewayClient gateway,
    ILogger<ProcessPaymentHandler> logger)
{
    public async Task<PaymentResult> HandleAsync(
        ProcessPaymentCommand command, CancellationToken ct)
    {
        var payment = Payment.Create(
            command.CustomerId,
            command.MerchantId,
            command.Amount,
            command.Currency,
            command.PaymentMethodId);

        try
        {
            var charge = await gateway.AuthorizeAsync(
                command.Amount, command.Currency, command.PaymentMethodId,
                command.IdempotencyKey, ct);

            payment.MarkCharged(charge.TransactionId);
            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Payment {PaymentId} charged via gateway ({GatewayTxn})",
                payment.Id, charge.TransactionId);

            return new PaymentResult(payment.Id, payment.Status, charge.TransactionId, null, DateTime.UtcNow);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "Payment {PaymentId} failed at the gateway", payment.Id);
            payment.MarkFailed(ex.Message);
            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);
            return new PaymentResult(payment.Id, payment.Status, null, ex.Message, DateTime.UtcNow);
        }
    }

    public Task<PaymentResult?> GetAsync(Guid paymentId, CancellationToken ct)
        => db.Payments
            .Where(p => p.Id == paymentId)
            .Select(p => new PaymentResult(
                p.Id, p.Status, p.GatewayTransactionId, p.DeclineReason, p.ProcessedAtUtc))
            .SingleOrDefaultAsync(ct);
}
