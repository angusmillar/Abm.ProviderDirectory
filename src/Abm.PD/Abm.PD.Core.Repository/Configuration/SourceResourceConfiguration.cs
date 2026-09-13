using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class SourceResourceConfiguration : IEntityTypeConfiguration<SourceResource>
{
    // FHIR's id datatype is capped at 64 characters by the specification.
    private const int ResourceIdMaxLength = 64;
    private const int ResourceTypeMaxLength = 50;

    // JobId's shape is the source server's to define - it is parsed out of a Location header rather
    // than issued by us - so it gets a generous bound rather than reusing EntityConfigurationConstants.CodeMaxLength.
    private const int JobIdMaxLength = 200;

    public void Configure(EntityTypeBuilder<SourceResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("source_resource");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JobId)
            .HasMaxLength(JobIdMaxLength);

        builder.Property(x => x.ResourceType)
            .HasMaxLength(ResourceTypeMaxLength);

        builder.Property(x => x.ResourceId)
            .HasMaxLength(ResourceIdMaxLength);

        // jsonb rather than text: Npgsql maps it straight onto this string property, it validates the
        // payload is well formed JSON on write, and it leaves a GIN index reachable later without a
        // migration if a query need ever shows up - none of which requires the entity to know about it.
        builder.Property(x => x.Resource)
            .HasColumnType("jsonb");

        // One row per resource per job. This single unique index also serves the two read patterns
        // needed - "everything for a JobId" and "this job's copy of this resource" - through the
        // leftmost prefix, so no second index is carried just for the JobId-only lookup.
        builder.HasIndex(x => new { x.JobId, x.ResourceType, x.ResourceId })
            .IsUnique();

        builder.Property(x => x.DataSourceId)
            .IsRequired();

        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
