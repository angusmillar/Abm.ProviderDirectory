using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

public record MatchingTaskRequest(
    string Code,
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int? MaxRunCount,
    string MetaData);
