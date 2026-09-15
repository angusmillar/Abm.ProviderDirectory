using Abm.Core.Time;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// The shape returned by GET/GET-search (and echoed back by POST/PUT) for an ExportTask.
/// Every timestamp is converted from its UTC storage into the configured ServiceDefaultTimeZone and
/// surfaced as a DateTimeOffset, so API consumers see it as wall-clock local time without losing the
/// absolute instant. This response can be PUT straight back to <c>/ExportTask/{id}</c> -
/// <see cref="ExportTaskUpdateRequest"/> deliberately omits the server-controlled fields here
/// (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc, FailureCount, RunCount,
/// LastCorrelationId) so they are silently ignored rather than erroring the update. DataSourceCode is
/// present on both, but immutable after creation - the update handler rejects a PUT that tries to
/// change it.
/// </summary>
public record ExportTaskResponse(
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
    string DataSourceCode,
    ExportTaskParameterRequest Parameter)
{
    public static ExportTaskResponse FromEntity(ExportTask exportTask, IDateTimeProvider dateTimeProvider)
    {
        return new ExportTaskResponse(
            Id: exportTask.Id,
            TypeId: exportTask.TypeId,
            Code: exportTask.Code,
            DisplayName: exportTask.DisplayName,
            Description: exportTask.Description,
            State: exportTask.State,
            StateReason: exportTask.StateReason,
            TriggerEvery: exportTask.TriggerEvery,
            StartAtUtc: dateTimeProvider.ToServiceOffset(exportTask.StartAtUtc),
            EndAtUtc: dateTimeProvider.ToServiceOffset(exportTask.EndAtUtc),
            CreatedUtc: dateTimeProvider.ToServiceOffset(exportTask.CreatedUtc),
            UpdatedUtc: dateTimeProvider.ToServiceOffset(exportTask.UpdatedUtc),
            LastStartUtc: dateTimeProvider.ToServiceOffset(exportTask.LastStartUtc),
            LastEndUtc: dateTimeProvider.ToServiceOffset(exportTask.LastEndUtc),
            FailureCount: exportTask.FailureCount,
            RunCount: exportTask.RunCount,
            MaxRunCount: exportTask.MaxRunCount,
            LastCorrelationId: exportTask.LastCorrelationId,
            DataSourceCode: exportTask.DataSource.Code,
            Parameter: new ExportTaskParameterRequest(
                Type: exportTask.Parameter.Type,
                Since: exportTask.Parameter.Since?.ToOffset(dateTimeProvider.ServiceDefaultTimeZone),
                TypeFilterList: exportTask.Parameter.TypeFilterList));
    }
}
