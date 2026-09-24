using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ProviderDirectoryDbContext(DbContextOptions<ProviderDirectoryDbContext> options)
    : DbContext(options)
{
    public DbSet<DataSource> DataSource => Set<DataSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProviderDirectoryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
