using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Contracts;

/// <summary>
/// PUT body shape for updating an ExportLoaderTask. Deliberately excludes the server-controlled
/// fields that <see cref="ExportLoaderTaskResponse"/> returns (Id, TypeId, Code, DataSourceId,
/// DataSource, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc) so the exact JSON body returned by a
/// prior GET can be PUT straight back without editing it first - System.Text.Json ignores the extra
/// properties rather than erroring on them.
/// </summary>
public record ExportLoaderTaskUpdateRequest(
    string DisplayName,
    string? Description,
    TaskStateId State,
    string? StateReason,
    TimeSpan TriggerEvery,
    DateTimeOffset? ToStartAtUtc,
    DateTimeOffset? ToEndAtUtc,
    ExportLoaderTaskParameterRequest Parameter);
