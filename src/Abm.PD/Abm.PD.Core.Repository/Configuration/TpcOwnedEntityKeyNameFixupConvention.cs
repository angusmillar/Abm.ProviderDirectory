using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Abm.PD.Core.Repository.Configuration;

// EFCore.NamingConventions deliberately does not rewrite primary key constraint names on a
// TPC-mapped entity type (see efcore/EFCore.NamingConventions#57 - rewriting is disabled for TPT,
// and empirically TPC too, because of unresolved upstream EF Core issues), but it does still rewrite
// the shadow primary key that an owned entity gets when it table-splits into that same TPC table
// (see ExportLoaderTaskConfiguration's OwnsOne(x => x.Parameter)). Left alone, the two halves of one
// physical table end up with two different primary key constraint names and EF refuses to build
// the model at all ("table 'x' cannot be used for entity type ... since the name of the primary
// key ... does not match ..."). Registered in ProviderDirectoryDbContext.ConfigureConventions so it
// runs after the naming convention plugin's own model-finalizing convention, this re-aligns the
// TPC side's key name to whatever name the owned side already received.
//
// principalKey.Builder.HasName(ownedKeyName) below writes a HIERARCHY-WIDE primary-key name
// annotation on the root TaskBase entity type, not a per-table one - EF has no concept of a
// per-concrete-table PK name override on the shared root type. That is harmless while exactly one
// concrete table in the hierarchy has a table-split owned entity (today: only export_loader_task),
// because there is only one name to align to. It becomes actively wrong the moment a second
// TaskBase subtype exists: that subtype's migration would scaffold with the PK constraint name
// copied from the first subtype's table, and Postgres rejects the collision ("relation ... already
// exists"). Adding a second TaskBase subtype is out of scope for this plan, so rather than solving
// the general N-subtype case here, this guard makes the hazard loud instead of letting it silently
// produce a broken migration - see TpcOwnedEntityKeyNameFixupConvention (this class) for whoever
// hits it next. Fixing it properly means either scoping the alignment per concrete table, or
// reversing the alignment direction (accept PascalCase EF-default names on the owned side instead).
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

            // The HasName() call below sets a hierarchy-wide PK name annotation on
            // principalEntityType's root, not a per-table one, so it only gives correct results
            // when exactly one concrete table in this TPC hierarchy has a table-split owned entity.
            // principalEntityType here is the concrete leaf (e.g. ExportLoaderTask), and
            // GetDerivedTypesInclusive() only walks DESCENDANTS of the type it is called on - a
            // sibling subtype added later (e.g. AnotherTask : TaskBase) would not be a descendant of
            // ExportLoaderTask, so counting from the leaf would always see just itself. Walk up to
            // the shared hierarchy root first (TaskBase), then count distinct concrete tables across
            // ALL of the root's descendants, and refuse to silently misbehave once there is more
            // than one.
            IConventionEntityType hierarchyRoot = principalEntityType.GetRootType();
            int concreteTableCount = hierarchyRoot.GetDerivedTypesInclusive()
                .Where(t => !t.IsAbstract())
                .Select(t => (t.GetTableName(), t.GetSchema()))
                .Distinct()
                .Count();
            if (concreteTableCount > 1)
            {
                throw new NotSupportedException(
                    $"{nameof(TpcOwnedEntityKeyNameFixupConvention)} cannot align the primary key "
                    + $"name for the table-split owned entity on '{entityType.DisplayName()}': "
                    + $"'{principalEntityType.DisplayName()}''s TPC hierarchy now maps to "
                    + $"{concreteTableCount} concrete tables, but this convention only supports "
                    + "exactly one concrete table in the hierarchy having a table-split owned "
                    + "entity - it writes a hierarchy-wide PK name annotation, not a per-table one, "
                    + "so a second concrete table would scaffold with the wrong (colliding) PK "
                    + "constraint name copied from the first. This convention's PK name alignment "
                    + "strategy needs revisiting before adding another TaskBase subtype - either "
                    + "scope the fix per concrete table, or reverse the alignment direction and "
                    + "accept the owned side's PascalCase EF-default name instead. See "
                    + $"{nameof(TpcOwnedEntityKeyNameFixupConvention)}.cs for the full explanation.");
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
