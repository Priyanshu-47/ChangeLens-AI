using ChangeLens.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChangeLens.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OccurredAtUtc).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
        builder.Property(a => a.ResourceType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.ResourceId).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.DetailsJson).HasColumnType("jsonb");

        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => new { a.ProjectId, a.OccurredAtUtc });
    }
}
