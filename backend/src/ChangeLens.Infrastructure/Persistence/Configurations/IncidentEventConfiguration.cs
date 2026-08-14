using ChangeLens.Domain.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChangeLens.Infrastructure.Persistence.Configurations;

public sealed class IncidentEventConfiguration : IEntityTypeConfiguration<IncidentEvent>
{
    public void Configure(EntityTypeBuilder<IncidentEvent> builder)
    {
        builder.ToTable("incident_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OccurredAtUtc).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Source).HasMaxLength(200);
        builder.Property(e => e.Message).HasMaxLength(4000);
        builder.Property(e => e.RawDataJson).HasColumnType("jsonb");

        builder.HasIndex(e => e.IncidentId);
        builder.HasIndex(e => new { e.IncidentId, e.OccurredAtUtc });
    }
}
