using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

public record ExportLoaderTaskRequest(
    string Code,
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTimeOffset? ToStartAtUtc,
    DateTimeOffset? ToEndAtUtc,
    string DataSourceCode,
    ExportLoaderTaskParameterRequest Parameter);
