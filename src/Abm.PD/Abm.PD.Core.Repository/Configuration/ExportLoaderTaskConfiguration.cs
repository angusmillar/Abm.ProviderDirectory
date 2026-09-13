using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ExportLoaderTaskConfiguration : IEntityTypeConfiguration<ExportLoaderTask>
{
    public void Configure(EntityTypeBuilder<ExportLoaderTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // TPH mapping otherwise nullifies a derived-type column regardless of the CLR property's own
        // non-nullable int type - explicit Property(...).IsRequired() is needed on top of the
        // relationship's IsRequired() to actually get a NOT NULL data_source_id column.
        builder.Property(x => x.DataSourceId)
            .IsRequired();

        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(x => x.Parameter, parameter =>
        {
            parameter.ToTable("export_loader_task_parameter");

            // The auto-generated name for this FK truncates at Postgres's 63-character identifier
            // limit (fk_export_loader_task_parameter_export_loader_tasks_export_loa) - name it
            // explicitly instead.
            parameter.WithOwner().HasConstraintName("fk_export_loader_task_parameter_task");

            parameter.Property(x => x.Type).HasColumnName("type");
            parameter.Property(x => x.Since).HasColumnName("since");
            parameter.Property(x => x.TypeFilterList).HasColumnName("type_filter_list");
        });
    }
}
