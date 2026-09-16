using Abm.Core.Time;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// The shape returned by GET/GET-search (and echoed back by POST/PUT) for a MatchingTask.
/// Every timestamp is converted from its UTC storage into the configured ServiceDefaultTimeZone and
/// surfaced as a DateTimeOffset, so API consumers see it as wall-clock local time without losing the
/// absolute instant. This response can be PUT straight back to <c>/MatchingTask/{id}</c> -
/// <see cref="MatchingTaskUpdateRequest"/> deliberately omits the server-controlled fields here
/// (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc, FailureCount, RunCount,
/// LastCorrelationId) so they are silently ignored rather than erroring the update.
/// </summary>
public record MatchingTaskResponse(
    int Id,
    TaskTypeId TypeId,
    string Code,
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastStartUtc,
    DateTimeOffset? LastEndUtc,
    int FailureCount,
    int RunCount,
    int? MaxRunCount,
    Guid? LastCorrelationId,
    string MetaData)
{
    public static MatchingTaskResponse FromEntity(MatchingTask matchingTask, IDateTimeProvider dateTimeProvider)
    {
        return new MatchingTaskResponse(
            Id: matchingTask.Id,
            TypeId: matchingTask.TypeId,
            Code: matchingTask.Code,
            DisplayName: matchingTask.DisplayName,
            Description: matchingTask.Description,
            State: matchingTask.State,
            StateReason: matchingTask.StateReason,
            TriggerEvery: matchingTask.TriggerEvery,
            StartAtUtc: dateTimeProvider.ToServiceOffset(matchingTask.StartAtUtc),
            EndAtUtc: dateTimeProvider.ToServiceOffset(matchingTask.EndAtUtc),
            CreatedUtc: dateTimeProvider.ToServiceOffset(matchingTask.CreatedUtc),
            UpdatedUtc: dateTimeProvider.ToServiceOffset(matchingTask.UpdatedUtc),
            LastStartUtc: dateTimeProvider.ToServiceOffset(matchingTask.LastStartUtc),
            LastEndUtc: dateTimeProvider.ToServiceOffset(matchingTask.LastEndUtc),
            FailureCount: matchingTask.FailureCount,
            RunCount: matchingTask.RunCount,
            MaxRunCount: matchingTask.MaxRunCount,
            LastCorrelationId: matchingTask.LastCorrelationId,
            MetaData: matchingTask.MetaData);
    }
}
