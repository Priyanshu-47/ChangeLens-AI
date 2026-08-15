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
        builder.Property(r => r.Model).HasMaxLength(200);
        builder.Property(r => r.PromptVersion).HasMaxLength(100);
        builder.Property(r => r.Error).HasMaxLength(2000);

        builder.HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.ProjectId);
        builder.HasIndex(r => new { r.ProjectId, r.CreatedAtUtc });
    }
}
