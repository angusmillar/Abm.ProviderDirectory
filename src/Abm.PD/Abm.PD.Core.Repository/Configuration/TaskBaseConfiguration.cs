using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskBaseConfiguration : IEntityTypeConfiguration<TaskBase>
{
    public void Configure(EntityTypeBuilder<TaskBase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table-Per-Hierarchy: every TaskBase subtype (ExportTask, MatchingTask today) shares this
        // one table, discriminated by the stored TypeId column - see the design spec's TPH section
        // for why this replaced the earlier TPC decision.
        builder.ToTable("task");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.HasDiscriminator(x => x.TypeId)
            .HasValue<ExportTask>(TaskTypeId.ExportTask)
            .HasValue<MatchingTask>(TaskTypeId.MatchingTask);
    }
}
