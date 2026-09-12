using Abm.PD.Core.Repository.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Abm.PD.Core.Api.Tests.Conventions;

// Regression coverage for TpcOwnedEntityKeyNameFixupConvention's "more than one concrete table in
// the hierarchy has a table-split owned entity" guard. A previous fix attempt added that guard but
// counted concrete tables from the wrong entity type (the concrete leaf principal rather than the
// hierarchy root), which meant GetDerivedTypesInclusive() only ever saw the leaf itself and the
// guard could never fire - see TpcOwnedEntityKeyNameFixupConvention.cs for the corrected logic.
// These tests build synthetic, test-only EF models that reproduce the exact hazardous shape (a TPC
// hierarchy with two concrete subtypes, each with its own table-split owned entity) without adding
// a second real TaskBase subtype to the actual domain model, which remains out of scope for this
// plan.
public class TpcOwnedEntityKeyNameFixupConventionTests
{
    private const string UnusedConnectionString = "Host=unused;Database=unused;Username=unused;Password=unused";

    private abstract class SyntheticTaskBase
    {
        public int Id { get; set; }
    }

    private sealed class SyntheticTaskA : SyntheticTaskBase
    {
        public required SyntheticOwned Owned { get; set; }
    }

    private sealed class SyntheticTaskB : SyntheticTaskBase
    {
        public required SyntheticOwned Owned { get; set; }
    }

    private sealed class SyntheticOwned
    {
        public required string Value { get; set; }
    }

    // Two concrete TaskBase subtypes, each table-splitting its own owned entity into its own table -
    // the shape the guard exists to catch.
    private sealed class TwoSubtypesDbContext(DbContextOptions<TwoSubtypesDbContext> options)
        : DbContext(options)
    {
        public DbSet<SyntheticTaskA> TasksA => Set<SyntheticTaskA>();

        public DbSet<SyntheticTaskB> TasksB => Set<SyntheticTaskB>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyntheticTaskBase>().UseTpcMappingStrategy();

            modelBuilder.Entity<SyntheticTaskA>(builder =>
            {
                builder.ToTable("synthetic_task_a");
                builder.OwnsOne(x => x.Owned, owned => owned.ToTable("synthetic_task_a"));
            });

            modelBuilder.Entity<SyntheticTaskB>(builder =>
            {
                builder.ToTable("synthetic_task_b");
                builder.OwnsOne(x => x.Owned, owned => owned.ToTable("synthetic_task_b"));
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.Conventions.Add(_ => new TpcOwnedEntityKeyNameFixupConvention());
        }
    }

    // A single concrete TaskBase subtype with a table-split owned entity - today's actual
    // ExportLoaderTask shape. The guard must not fire for this case.
    private sealed class SingleSubtypeDbContext(DbContextOptions<SingleSubtypeDbContext> options)
        : DbContext(options)
    {
        public DbSet<SyntheticTaskA> TasksA => Set<SyntheticTaskA>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyntheticTaskBase>().UseTpcMappingStrategy();

            modelBuilder.Entity<SyntheticTaskA>(builder =>
            {
                builder.ToTable("synthetic_task_a");
                builder.OwnsOne(x => x.Owned, owned => owned.ToTable("synthetic_task_a"));
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.Conventions.Add(_ => new TpcOwnedEntityKeyNameFixupConvention());
        }
    }

    [Fact]
    public void TwoConcreteSubtypesEachWithTableSplitOwnedEntity_ThrowsInsteadOfSilentlyMisnamingKeys()
    {
        DbContextOptions<TwoSubtypesDbContext> options = new DbContextOptionsBuilder<TwoSubtypesDbContext>()
            .UseNpgsql(UnusedConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        using TwoSubtypesDbContext context = new(options);

        // Accessing .Model triggers full model building (incl. ProcessModelFinalizing conventions)
        // without ever opening a connection - no Docker/Postgres needed for this test.
        Assert.Throws<NotSupportedException>(() => _ = context.Model);
    }

    [Fact]
    public void OneConcreteSubtypeWithTableSplitOwnedEntity_BuildsSuccessfully()
    {
        DbContextOptions<SingleSubtypeDbContext> options = new DbContextOptionsBuilder<SingleSubtypeDbContext>()
            .UseNpgsql(UnusedConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        using SingleSubtypeDbContext context = new(options);

        // Sanity check: the guard must NOT fire for today's actual shape (exactly one concrete
        // table with a table-split owned entity) - this is what ExportLoaderTask looks like today.
        // Model building must complete without throwing.
        IModel model = context.Model;

        Assert.NotNull(model);
    }
}
