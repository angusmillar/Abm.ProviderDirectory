using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.Core.Extensions;
using Abm.Core.Time;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// The shape returned by GET/GET-search (and echoed back by POST/PUT) for an ExportTask.
/// Every timestamp is converted from its UTC storage into the configured ServiceDefaultTimeZone and
/// surfaced as a DateTimeOffset, so API consumers see it as wall-clock local time without losing the
/// absolute instant. This response can be PUT straight back to <c>/ExportTask/{id}</c> -
/// <see cref="ExportTaskUpdateRequest"/> deliberately omits the server-controlled fields here
/// (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc) so they are silently ignored
/// rather than erroring the update. DataSourceCode is present on both, but immutable after creation -
/// the update handler rejects a PUT that tries to change it.
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
    DateTimeOffset? ToStartAtUtc,
    DateTimeOffset? ToEndAtUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastStartUtc,
    DateTimeOffset? LastEndUtc,
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
            ToStartAtUtc: dateTimeProvider.ToServiceOffset(exportTask.ToStartAtUtc),
            ToEndAtUtc: dateTimeProvider.ToServiceOffset(exportTask.ToEndAtUtc),
            CreatedUtc: dateTimeProvider.ToServiceOffset(exportTask.CreatedUtc),
            UpdatedUtc: dateTimeProvider.ToServiceOffset(exportTask.UpdatedUtc),
            LastStartUtc: dateTimeProvider.ToServiceOffset(exportTask.LastStart),
            LastEndUtc: dateTimeProvider.ToServiceOffset(exportTask.LastEnd),
            DataSourceCode: exportTask.DataSource.Code,
            Parameter: new ExportTaskParameterRequest(
                Type: exportTask.Parameter.Type,
                Since: exportTask.Parameter.Since?.ToOffset(dateTimeProvider.ServiceDefaultTimeZone),
                TypeFilterList: exportTask.Parameter.TypeFilterList));
    }

}
