using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// PUT body shape for updating an ExportTask. Deliberately excludes the server-controlled
/// fields that <see cref="ExportTaskResponse"/> returns (Id, TypeId, Code, CreatedUtc,
/// UpdatedUtc, LastStartUtc, LastEndUtc, FailureCount, RunCount, LastCorrelationId) so the exact JSON
/// body returned by a prior GET can be PUT straight back without editing it first -
/// System.Text.Json ignores the extra properties rather than erroring on them. DataSourceCode is
/// carried over rather than excluded - it round-trips unchanged, but the handler rejects a PUT that
/// tries to change it since it is immutable after creation.
/// </summary>
public record ExportTaskUpdateRequest(
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    string DataSourceCode,
    TimeSpan TriggerEvery,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int? MaxRunCount,
    ExportTaskParameterRequest Parameter);
