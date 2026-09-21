namespace Abm.PD.Core.Domain.Projections;

/// <summary>
/// One entry of ISourceResourceRepository.GetSourceToTargetResourceIdDictionaryAsync's result, keyed by the
/// source reference "{ResourceType}/{ResourceId}". Id, ResourceType and ResourceId are read straight off the
/// SourceResource row.
///
/// AssignedTargetResourceId always comes back null from the repository - SourceResource carries no such
/// column, so there is nothing in the database for it to reflect. It is a settable property, rather than part
/// of the constructor, precisely so that a caller (MatchingTaskRunner) must explicitly assign it before use -
/// the name and the null starting value both say the same thing: this id is generated on the fly, not read
/// from storage.
/// </summary>
public sealed record SourceToTargetResourceIdLookup
{
    public required int Id { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    public string? AssignedTargetResourceId { get; set; }
}
