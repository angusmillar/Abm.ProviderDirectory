using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class DataSourceConfiguration : IEntityTypeConfiguration<DataSource>
{
    public void Configure(EntityTypeBuilder<DataSource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("data_source");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);
        builder.Property(x => x.DisplayName);

        builder.HasIndex(x => x.Code)
            .IsUnique();
    }
}
