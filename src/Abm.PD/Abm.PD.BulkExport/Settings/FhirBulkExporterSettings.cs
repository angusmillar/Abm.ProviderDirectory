namespace Abm.PD.BulkExport.Settings;

public record FhirBulkExporterSettings
{
    public const string SectionName = "FhirBulkExporter";

    /// <summary>
    /// The HttpClient.Timeout used while streaming the export's output files. It is end-to-end, so it bounds how
    /// long GetExport may take to stream an export of any size — 2 hours by default, longer than any export this
    /// solution has seen but still a backstop rather than no limit at all. Override via config if an export needs
    /// longer, or use the CancellationToken to stop the work sooner.
    /// </summary>
    public TimeSpan StreamedExportHttpClientTimeout { get; init; } = TimeSpan.FromHours(2);
}
