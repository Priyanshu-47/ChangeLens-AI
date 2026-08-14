using ChangeLens.Domain.Projects;
using ChangeLens.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChangeLens.Infrastructure.Persistence.Configurations;

public sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> builder)
    {
        builder.ToTable("repositories");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Url).HasMaxLength(500).IsRequired();
        builder.Property(r => r.DefaultBranch).HasMaxLength(100);
        builder.Property(r => r.Language).HasMaxLength(50).IsRequired();

        builder.HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.ProjectId);
        builder.HasIndex(r => new { r.ProjectId, r.Name });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
