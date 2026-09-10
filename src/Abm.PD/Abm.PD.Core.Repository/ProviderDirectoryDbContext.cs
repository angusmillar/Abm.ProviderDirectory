using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ProviderDirectoryDbContext(DbContextOptions<ProviderDirectoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Resource> Resources => Set<Resource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // EF Core's default table-naming convention takes the DbSet property name (Resources,
        // plural); snake-casing alone would produce "resources". Pin it singular explicitly.
        modelBuilder.Entity<Resource>().ToTable("resource");
    }
}
