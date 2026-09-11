using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Abm.PD.Core.Repository.Configuration;

// EFCore.NamingConventions deliberately does not rewrite primary key constraint names on a
// TPC-mapped entity type (see efcore/EFCore.NamingConventions#57 - rewriting is disabled for TPT
// and TPC because of unresolved upstream EF Core issues), but it does still rewrite the shadow
// primary key that an owned entity gets when it table-splits into that same TPC table (see
// ExportLoaderTaskConfiguration's OwnsOne(x => x.Parameter)). Left alone, the two halves of one
// physical table end up with two different primary key constraint names and EF refuses to build
// the model at all ("table 'x' cannot be used for entity type ... since the name of the primary
// key ... does not match ..."). Registered in ProviderDirectoryDbContext.ConfigureConventions so it
// runs after the naming convention plugin's own model-finalizing convention, this re-aligns the
// TPC side's key name to whatever name the owned side already received.
internal sealed class TpcOwnedEntityKeyNameFixupConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            IConventionForeignKey? ownership = entityType.FindOwnership();
            if (ownership is null)
            {
                continue;
            }

            IConventionEntityType principalEntityType = ownership.PrincipalEntityType;
            if (entityType.GetTableName() != principalEntityType.GetTableName()
                || entityType.GetSchema() != principalEntityType.GetSchema())
            {
                // Not table-split - nothing to reconcile.
                continue;
            }

            IConventionKey? ownedKey = entityType.FindPrimaryKey();
            IConventionKey? principalKey = principalEntityType.FindPrimaryKey();
            string? ownedKeyName = ownedKey?.GetName();
            string? principalKeyName = principalKey?.GetName();

            if (ownedKeyName is not null
                && principalKey is not null
                && ownedKeyName != principalKeyName)
            {
                principalKey.Builder.HasName(ownedKeyName);
            }
        }
    }
}
