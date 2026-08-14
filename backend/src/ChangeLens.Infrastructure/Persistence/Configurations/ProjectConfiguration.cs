using ChangeLens.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChangeLens.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(140).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => p.Name);

        // Soft delete: deleted projects remain in the database for history.
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
