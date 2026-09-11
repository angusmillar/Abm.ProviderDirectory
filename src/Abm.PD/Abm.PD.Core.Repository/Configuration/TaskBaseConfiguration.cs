using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class TaskBaseConfiguration : IEntityTypeConfiguration<TaskBase>
{
    public void Configure(EntityTypeBuilder<TaskBase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table-Per-Concrete-Type: TaskBase has no table of its own. Each concrete task type
        // (ExportLoaderTask today) gets its own table carrying every TaskBase column plus its own -
        // see the design spec's TPC section for why (no cross-task-type querying is needed yet).
        builder.UseTpcMappingStrategy();
    }
}
