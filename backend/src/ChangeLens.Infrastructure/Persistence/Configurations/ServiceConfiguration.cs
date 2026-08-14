using ChangeLens.Domain.Projects;
using ChangeLens.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChangeLens.Infrastructure.Persistence.Configurations;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Language).HasMaxLength(50);
        builder.Property(s => s.RootPath).HasMaxLength(500);

        builder.HasOne(s => s.Project)
            .WithMany()
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.ProjectId);
        builder.HasIndex(s => new { s.ProjectId, s.Name });
    }
}
