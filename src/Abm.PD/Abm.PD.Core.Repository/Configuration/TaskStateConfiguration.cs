using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskStateConfiguration : IEntityTypeConfiguration<TaskState>
{
    public void Configure(EntityTypeBuilder<TaskState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("task_state");
        builder.HasKey(x => x.TaskStateId);
        builder.Property(x => x.TaskStateId).HasConversion<int>();
        builder.Property(x => x.Name)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

        builder.HasData(
            Enum.GetValues(typeof(TaskStateId))
                .Cast<TaskStateId>()
                .Select(e => new TaskState(e, e.ToString())));
    }
}
