using AcmePay.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace AcmePay.Infrastructure.Persistence;

/// <summary>
/// Synthetic demo database context. PostgreSQL is used in the demo config
/// (matching the ChangeLens backend stack).
/// </summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(p => p.GatewayTransactionId).HasMaxLength(64);
            entity.HasIndex(p => p.MerchantId);
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            entity.Property(r => r.Amount).HasPrecision(18, 2);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(r => r.GatewayRefundId).HasMaxLength(64);
            entity.HasIndex(r => r.PaymentId);
        });
    }
}
