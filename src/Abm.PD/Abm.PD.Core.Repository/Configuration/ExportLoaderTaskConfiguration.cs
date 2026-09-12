using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ExportLoaderTaskConfiguration : IEntityTypeConfiguration<ExportLoaderTask>
{
    public void Configure(EntityTypeBuilder<ExportLoaderTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.OwnsOne(x => x.Parameter, parameter =>
        {
            parameter.ToTable("export_loader_task_parameter");
            parameter.Property(x => x.Type).HasColumnName("type");
            parameter.Property(x => x.Since).HasColumnName("since");
            parameter.Property(x => x.TypeFilterList).HasColumnName("type_filter_list");
        });
    }
}
