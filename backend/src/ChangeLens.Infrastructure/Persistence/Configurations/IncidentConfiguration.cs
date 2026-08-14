using ChangeLens.Domain.Incidents;
using ChangeLens.Domain.Projects;
using ChangeLens.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChangeLens.Infrastructure.Persistence.Configurations;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Title).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Severity).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Classification).HasMaxLength(200);
        builder.Property(i => i.Environment).HasMaxLength(100);
        builder.Property(i => i.Summary).HasMaxLength(4000);
        builder.Property(i => i.StartedAtUtc).IsRequired();

        builder.HasOne(i => i.Project)
            .WithMany()
            .HasForeignKey(i => i.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.AffectedService)
            .WithMany()
            .HasForeignKey(i => i.AffectedServiceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(i => i.Events)
            .WithOne(e => e.Incident)
            .HasForeignKey(e => e.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.ProjectId);
        builder.HasIndex(i => new { i.ProjectId, i.AffectedServiceId });
        builder.HasIndex(i => new { i.ProjectId, i.StartedAtUtc });
    }
}
