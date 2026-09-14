using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ExportTaskConfiguration : IEntityTypeConfiguration<ExportTask>
{
    public void Configure(EntityTypeBuilder<ExportTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // IsRequired() here only enforces non-null at the CLR/model layer - a materialised ExportTask
        // with a null DataSourceId would fail CLR binding, since DataSourceId is a non-nullable int.
        // It does not make the generated data_source_id column NOT NULL: EF Core's TPH base-table
        // convention keeps every derived type's columns nullable at the DDL level regardless of a
        // derived-type IsRequired() call, so the column stays nullable (confirmed by the regenerated
        // migration, which emits data_source_id as nullable: true).
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
