using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// PUT body shape for updating a SeedDirectoryTask. Deliberately excludes the server-controlled
/// fields that <see cref="SeedDirectoryTaskResponse"/> returns (Id, TypeId, Code, CreatedUtc,
/// UpdatedUtc, LastStartUtc, LastEndUtc, FailureCount, RunCount, LastCorrelationId) so the exact JSON
/// body returned by a prior GET can be PUT straight back without editing it first -
/// System.Text.Json ignores the extra properties rather than erroring on them.
/// </summary>
public record SeedDirectoryTaskUpdateRequest(
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int? MaxRunCount,
    string MetaData);
