namespace Abm.PD.Core.Application.Loader;

/// <summary>
/// The outcome of one load. <see cref="RetainedFailures"/> holds only the first
/// SourceResourceLoaderSettings.MaxRetainedFailures failures — <see cref="FailedCount"/> counts them all, because
/// a load that fails on every resource must not grow a list in proportion to the size of the export.
/// </summary>
public sealed record SourceResourceLoadResult(
    long SubmittedCount,
    long CommittedCount,
    long FailedCount,
    int BatchCount,
    IReadOnlyList<SourceResourceLoadFailure> RetainedFailures);
