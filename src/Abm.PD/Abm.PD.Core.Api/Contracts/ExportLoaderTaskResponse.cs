using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// The shape returned by GET/GET-search (and echoed back by POST/PUT) for an ExportLoaderTask.
/// Every timestamp is converted from its UTC storage into the configured ServiceDefaultTimeZone and
/// surfaced as a DateTimeOffset, so API consumers see it as wall-clock local time without losing the
/// absolute instant. This response can be PUT straight back to <c>/ExportLoaderTask/{id}</c> -
/// <see cref="ExportLoaderTaskUpdateRequest"/> deliberately omits the server-controlled fields here
/// (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc) so they are silently ignored
/// rather than erroring the update. DataSourceCode is present on both, but immutable after creation -
/// the update handler rejects a PUT that tries to change it.
/// </summary>
public record ExportLoaderTaskResponse(
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
    ExportLoaderTaskParameterRequest Parameter)
{
    public static ExportLoaderTaskResponse FromEntity(ExportLoaderTask exportLoaderTask, TimeSpan serviceDefaultTimeZone)
    {
        return new ExportLoaderTaskResponse(
            Id: exportLoaderTask.Id,
            TypeId: exportLoaderTask.TypeId,
            Code: exportLoaderTask.Code,
            DisplayName: exportLoaderTask.DisplayName,
            Description: exportLoaderTask.Description,
            State: exportLoaderTask.State,
            StateReason: exportLoaderTask.StateReason,
            TriggerEvery: exportLoaderTask.TriggerEvery,
            ToStartAtUtc: ToServiceOffset(exportLoaderTask.ToStartAtUtc, serviceDefaultTimeZone),
            ToEndAtUtc: ToServiceOffset(exportLoaderTask.ToEndAtUtc, serviceDefaultTimeZone),
            CreatedUtc: ToServiceOffset(exportLoaderTask.CreatedUtc, serviceDefaultTimeZone),
            UpdatedUtc: ToServiceOffset(exportLoaderTask.UpdatedUtc, serviceDefaultTimeZone),
            LastStartUtc: ToServiceOffset(exportLoaderTask.LastStart, serviceDefaultTimeZone),
            LastEndUtc: ToServiceOffset(exportLoaderTask.LastEnd, serviceDefaultTimeZone),
            DataSourceCode: exportLoaderTask.DataSource.Code,
            Parameter: new ExportLoaderTaskParameterRequest(
                Type: exportLoaderTask.Parameter.Type,
                Since: exportLoaderTask.Parameter.Since?.ToOffset(serviceDefaultTimeZone),
                TypeFilterList: exportLoaderTask.Parameter.TypeFilterList));
    }

    private static DateTimeOffset ToServiceOffset(DateTime utcDateTime, TimeSpan serviceDefaultTimeZone) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)).ToOffset(serviceDefaultTimeZone);

    private static DateTimeOffset? ToServiceOffset(DateTime? utcDateTime, TimeSpan serviceDefaultTimeZone) =>
        utcDateTime is null ? null : ToServiceOffset(utcDateTime.Value, serviceDefaultTimeZone);
}
