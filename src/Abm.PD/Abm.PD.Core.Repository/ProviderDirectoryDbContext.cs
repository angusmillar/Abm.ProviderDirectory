using Abm.PD.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ProviderDirectoryDbContext(DbContextOptions<ProviderDirectoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<DataSource> DataSource => Set<DataSource>();
    public DbSet<TaskState> TaskStates => Set<TaskState>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<ExportLoaderTask> ExportLoaderTasks => Set<ExportLoaderTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProviderDirectoryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
