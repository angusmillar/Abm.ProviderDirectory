using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Repository.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Abm.PD.Core.Repository;

public class ProviderDirectoryDbContext(DbContextOptions<ProviderDirectoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ProviderDataSource> ProviderDataSource => Set<ProviderDataSource>();
    public DbSet<TaskState> TaskStates => Set<TaskState>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<ExportLoaderTask> ExportLoaderTasks => Set<ExportLoaderTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProviderDirectoryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Must be added here, not as an IEntityTypeConfiguration - see
        // TpcOwnedEntityKeyNameFixupConvention's doc comment for why it needs to run after the
        // snake_case naming convention plugin's own model-finalizing convention.
        configurationBuilder.Conventions.Add(_ => new TpcOwnedEntityKeyNameFixupConvention());
    }
}
