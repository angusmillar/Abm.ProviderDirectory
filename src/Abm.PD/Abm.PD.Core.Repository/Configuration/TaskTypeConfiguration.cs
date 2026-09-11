using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskTypeConfiguration : IEntityTypeConfiguration<TaskType>
{
    public void Configure(EntityTypeBuilder<TaskType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("task_type");
        builder.HasKey(x => x.TaskTypeId);
        builder.Property(x => x.TaskTypeId).HasConversion<int>();
        builder.Property(x => x.Name)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

        builder.HasData(
            Enum.GetValues(typeof(TaskTypeId))
                .Cast<TaskTypeId>()
                .Select(e => new TaskType(e, e.ToString())));
    }
}
