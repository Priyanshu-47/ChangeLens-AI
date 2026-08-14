using AcmePay.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace AcmePay.Infrastructure.Persistence;

public sealed class PaymentsRepository(PaymentDbContext db)
{
    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Payments.SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Payment>> GetByMerchantAsync(Guid merchantId, CancellationToken ct = default) =>
        await db.Payments
            .Where(p => p.MerchantId == merchantId)
            .OrderByDescending(p => p.ProcessedAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(Payment payment, CancellationToken ct = default)
    {
        await db.Payments.AddAsync(payment, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        db.Payments.Update(payment);
        await db.SaveChangesAsync(ct);
    }
}
