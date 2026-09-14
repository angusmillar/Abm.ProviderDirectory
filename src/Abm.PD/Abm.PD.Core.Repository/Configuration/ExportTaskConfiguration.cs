using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ExportTaskConfiguration : IEntityTypeConfiguration<ExportTask>
{
    public void Configure(EntityTypeBuilder<ExportTask> builder)
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
            parameter.ToTable("export_task_parameter");

            // The auto-generated name for this FK truncates at Postgres's 63-character identifier
            // limit - name it explicitly instead.
            parameter.WithOwner().HasConstraintName("fk_export_task_parameter_task");

            // Pinned explicitly, carried over (renamed) from ExportLoaderTaskConfiguration - see
            // Task 2's Step 6. EF's naming convention for this owned type's shadow FK/PK resolves to a
            // bare "id" once TaskBase is reachable via ProviderDirectoryDbContext.Tasks; pinning it
            // keeps the physical column name consistent with the renamed entity/table rather than
            // leaving it as an unexplained bare "id".
            parameter.Property<int>("ExportTaskId").HasColumnName("export_task_id");

            parameter.Property(x => x.Type).HasColumnName("type");
            parameter.Property(x => x.Since).HasColumnName("since");
            parameter.Property(x => x.TypeFilterList).HasColumnName("type_filter_list");
        });
    }
}
