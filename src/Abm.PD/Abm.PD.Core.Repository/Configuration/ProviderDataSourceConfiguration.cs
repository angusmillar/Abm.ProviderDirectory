using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ProviderDataSourceConfiguration : IEntityTypeConfiguration<ProviderDataSource>
{
    public void Configure(EntityTypeBuilder<ProviderDataSource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("provider_data_source");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code)
            .HasMaxLength(EntityConfigurationConstants.CodeMaxLength);
        builder.Property(x => x.DisplayName);

        builder.HasIndex(x => x.Code)
            .IsUnique();
    }
}
