namespace Abm.PD.Core.Application.Loader;

public sealed record SourceResourceLoadFailure(
    string? ResourceType,
    string? ResourceId,
    Uri SourceUrl,
    long LineNumber,
    string[] ErrorMessages);
