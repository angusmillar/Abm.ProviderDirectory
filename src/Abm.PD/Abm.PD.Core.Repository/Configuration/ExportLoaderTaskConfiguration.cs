using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ExportLoaderTaskConfiguration : IEntityTypeConfiguration<ExportLoaderTask>
{
    public void Configure(EntityTypeBuilder<ExportLoaderTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("export_loader_task");

        // TypeId is a compile-time-constant computed property (see ExportLoaderTask.TypeId) - under
        // TPC the table itself already identifies the concrete type, so persisting it would just be
        // a constant column repeated on every row.
        builder.Ignore(x => x.TypeId);

        builder.Property(x => x.Code)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.OwnsOne(x => x.Parameter, parameter =>
        {
            parameter.Property(x => x.Type).HasColumnName("parameter_type");
            parameter.Property(x => x.Since).HasColumnName("parameter_since");
            parameter.Property(x => x.TypeFilterList).HasColumnName("parameter_type_filter_list");
        });
    }
}
