using Abm.Core.Time;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// The shape returned by GET/GET-search (and echoed back by POST/PUT) for a SeedDirectoryTask.
/// Every timestamp is converted from its UTC storage into the configured ServiceDefaultTimeZone and
/// surfaced as a DateTimeOffset, so API consumers see it as wall-clock local time without losing the
/// absolute instant. This response can be PUT straight back to <c>/SeedDirectoryTask/{id}</c> -
/// <see cref="SeedDirectoryTaskUpdateRequest"/> deliberately omits the server-controlled fields here
/// (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc, FailureCount, RunCount,
/// LastCorrelationId) so they are silently ignored rather than erroring the update.
/// </summary>
public record SeedDirectoryTaskResponse(
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
    public static SeedDirectoryTaskResponse FromEntity(SeedDirectoryTask seedDirectoryTask, IDateTimeProvider dateTimeProvider)
    {
        return new SeedDirectoryTaskResponse(
            Id: seedDirectoryTask.Id,
            TypeId: seedDirectoryTask.TypeId,
            Code: seedDirectoryTask.Code,
            DisplayName: seedDirectoryTask.DisplayName,
            Description: seedDirectoryTask.Description,
            State: seedDirectoryTask.State,
            StateReason: seedDirectoryTask.StateReason,
            TriggerEvery: seedDirectoryTask.TriggerEvery,
            StartAtUtc: dateTimeProvider.ToServiceOffset(seedDirectoryTask.StartAtUtc),
            EndAtUtc: dateTimeProvider.ToServiceOffset(seedDirectoryTask.EndAtUtc),
            CreatedUtc: dateTimeProvider.ToServiceOffset(seedDirectoryTask.CreatedUtc),
            UpdatedUtc: dateTimeProvider.ToServiceOffset(seedDirectoryTask.UpdatedUtc),
            LastStartUtc: dateTimeProvider.ToServiceOffset(seedDirectoryTask.LastStartUtc),
            LastEndUtc: dateTimeProvider.ToServiceOffset(seedDirectoryTask.LastEndUtc),
            FailureCount: seedDirectoryTask.FailureCount,
            RunCount: seedDirectoryTask.RunCount,
            MaxRunCount: seedDirectoryTask.MaxRunCount,
            LastCorrelationId: seedDirectoryTask.LastCorrelationId,
            MetaData: seedDirectoryTask.MetaData);
    }
}
