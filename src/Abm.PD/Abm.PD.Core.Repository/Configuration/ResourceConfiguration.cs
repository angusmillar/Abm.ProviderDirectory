using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abm.PD.Core.Repository.Configuration;

internal sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("resource");
        
    }
}
