using ChangeLens.Domain.Analysis;
using ChangeLens.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChangeLens.Infrastructure.Persistence.Configurations;

public sealed class AnalysisRunConfiguration : IEntityTypeConfiguration<AnalysisRun>
{
    public void Configure(EntityTypeBuilder<AnalysisRun> builder)
    {
        builder.ToTable("analysis_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.ChangeIdentifier).HasMaxLength(200);
        builder.Property(r => r.IncidentId);
        builder.Property(r => r.RequestId).HasMaxLength(200);
        builder.Property(r => r.Model).HasMaxLength(200);
        builder.Property(r => r.PromptVersion).HasMaxLength(100);
        builder.Property(r => r.RetrievalConfig);
        builder.Property(r => r.ResultJson).HasColumnType("jsonb");
        builder.Property(r => r.ResultSchemaVersion).HasMaxLength(50);
        builder.Property(r => r.FailureCode).HasMaxLength(50);
        builder.Property(r => r.Error).HasMaxLength(2000);

        builder.HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.ProjectId);
        builder.HasIndex(r => new { r.ProjectId, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.ProjectId, r.IncidentId });
        // Idempotency key: at most one outstanding (Queued/Running) submission per
        // project+requestId. Terminal runs may repeat the key — a re-submission then
        // starts a fresh investigation (api-contract.md §5.3, brief §8).
        builder.HasIndex(r => r.RequestId).IsUnique().HasFilter(
            "\"RequestId\" IS NOT NULL AND \"Status\" IN ('Queued', 'Running')");
    }
}
