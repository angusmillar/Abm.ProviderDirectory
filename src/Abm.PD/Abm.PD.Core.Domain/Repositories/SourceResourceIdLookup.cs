namespace Abm.PD.Core.Domain.Repositories;

/// <summary>
/// One entry of ISourceResourceRepository.GetResourceIdDictonaryAsync's result, keyed by the source reference
/// "{ResourceType}/{SourceResource.SourceResourceId}". SourceResourceId here is the SourceResource row's own
/// primary key (Id), not SourceResource.SourceResourceId - that natural source directory id is already the
/// dictionary key, so it is not repeated in the value.
/// </summary>
public sealed record SourceResourceIdLookup(
    string TargetResourceReference,
    int SourceResourceId);
