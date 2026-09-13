using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

public record ExportLoaderTaskRequest(
    string Code,
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTime? ToStartAtUtc,
    DateTime? ToEndAtUtc,
    int DataSourceId,
    ExportLoaderTaskParameterRequest Parameter);
