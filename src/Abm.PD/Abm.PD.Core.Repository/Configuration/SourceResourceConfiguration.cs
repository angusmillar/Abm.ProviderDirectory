using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class SourceResourceConfiguration : IEntityTypeConfiguration<SourceResource>
{
    // FHIR's id datatype is capped at 64 characters by the specification.
    private const int ResourceIdMaxLength = 64;
    private const int ResourceTypeMaxLength = 50;

    public void Configure(EntityTypeBuilder<SourceResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("source_resource");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ResourceType)
            .HasMaxLength(ResourceTypeMaxLength);

        builder.Property(x => x.SourceResourceId)
            .HasMaxLength(ResourceIdMaxLength);

        // jsonb rather than text: Npgsql maps it straight onto this string property, it validates the
        // payload is well-formed JSON on write, and it leaves a GIN index reachable later without a
        // migration if a query need ever shows up - none of which requires the entity to know about it.
        builder.Property(x => x.Resource)
            .HasColumnType("jsonb");

        // One row per resource per run. This single unique index also serves the two read patterns
        // needed - "everything for a CorrelationId" and "this run's copy of this resource" - through
        // the leftmost prefix, so no second index is carried just for the CorrelationId-only lookup.
        builder.HasIndex(x => new { x.CorrelationId, x.ResourceType, x.SourceResourceId })
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
