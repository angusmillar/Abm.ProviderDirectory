using Abm.PD.Tests.TestDoubles;

namespace Abm.PD.Tests.TestData;

/// <summary>
/// The canned Output Manifest and NDJSON payloads used across the export tests, written as the literal JSON a
/// server sends so that the manifest deserialisation is exercised against real wire shapes.
/// </summary>
public static class BulkExportTestData
{
    public const string TransactionTime = "2026-08-31T09:00:00+10:00";

    /// <summary>
    /// A manifest listing a single Practitioner output file.
    /// </summary>
    public static string SingleOutputFileManifest(
        string outputFileUrl = TestUrls.PractitionerOutputFileUrl,
        string resourceType = "Practitioner") =>
        $$"""
          {
            "transactionTime": "{{TransactionTime}}",
            "request": "{{TestUrls.ServiceBaseUrlWithSlash}}$export",
            "requiresAccessToken": true,
            "outputFormat": "application/fhir+ndjson",
            "output": [
              { "type": "{{resourceType}}", "url": "{{outputFileUrl}}", "count": 2 }
            ]
          }
          """;

    /// <summary>
    /// A manifest listing two output files, used to prove the files are read in manifest order and lazily.
    /// </summary>
    public static string TwoOutputFilesManifest() =>
        $$"""
          {
            "transactionTime": "{{TransactionTime}}",
            "requiresAccessToken": true,
            "output": [
              { "type": "Practitioner", "url": "{{TestUrls.PractitionerOutputFileUrl}}", "count": 2 },
              { "type": "Organization", "url": "{{TestUrls.OrganizationOutputFileUrl}}", "count": 1 }
            ]
          }
          """;

    /// <summary>
    /// A manifest with an empty output array, the shape returned when an export matched no resources.
    /// </summary>
    public static string EmptyOutputManifest() =>
        $$"""
          {
            "transactionTime": "{{TransactionTime}}",
            "requiresAccessToken": false,
            "output": []
          }
          """;

    /// <summary>
    /// A manifest with no output member at all.
    /// </summary>
    public static string NoOutputMemberManifest() =>
        $$"""
          {
            "transactionTime": "{{TransactionTime}}",
            "requiresAccessToken": false
          }
          """;

    public static string ManifestWithOutputFormat(
        string outputFormat) =>
        $$"""
          {
            "transactionTime": "{{TransactionTime}}",
            "requiresAccessToken": false,
            "outputFormat": "{{outputFormat}}",
            "output": [
              { "type": "Practitioner", "url": "{{TestUrls.PractitionerOutputFileUrl}}" }
            ]
          }
          """;

    /// <summary>
    /// A manifest exercising every member of the current ballot, including the outcome, deleted, link and
    /// extension members.
    /// </summary>
    public static string FullBallotManifest() =>
        $$"""
          {
            "manifestType": "http://hl7.org/fhir/uv/bulkdata/StructureDefinition/output-manifest",
            "transactionTime": "{{TransactionTime}}",
            "requiresAccessToken": true,
            "outputFormat": "application/fhir+ndjson",
            "outputOrganizedBy": "Patient",
            "outputOrganizedByDetail": "One file per compartment",
            "output": [
              {
                "type": "Practitioner",
                "url": "{{TestUrls.PractitionerOutputFileUrl}}",
                "continuesInFile": "{{TestUrls.OrganizationOutputFileUrl}}",
                "count": 12,
                "fileSize": 4096,
                "extension": { "https://example.test/custom": "value" }
              }
            ],
            "deleted": [
              { "type": "Bundle", "url": "{{TestUrls.ServiceBaseUrlWithSlash}}output/deleted-1.ndjson", "count": 3, "fileSize": 128 }
            ],
            "outcome": [
              {
                "type": "OperationOutcome",
                "url": "{{TestUrls.ServiceBaseUrlWithSlash}}output/outcome-1.ndjson",
                "count": 2,
                "fileSize": 256,
                "countSeverity": [
                  { "code": "warning", "count": 1 },
                  { "code": "information", "count": 1 }
                ]
              }
            ],
            "link": [
              { "relation": "next", "url": "{{TestUrls.ServiceBaseUrlWithSlash}}$export-poll-status?_jobId={{TestUrls.JobId}}&page=2" }
            ],
            "extension": { "https://example.test/server": { "region": "au" } }
          }
          """;

    /// <summary>
    /// A v2.0.0 manifest, which names the outcome files "error" rather than "outcome".
    /// </summary>
    public static string Version2Manifest() =>
        $$"""
          {
            "transactionTime": "{{TransactionTime}}",
            "request": "{{TestUrls.ServiceBaseUrlWithSlash}}$export",
            "requiresAccessToken": true,
            "output": [
              { "type": "Practitioner", "url": "{{TestUrls.PractitionerOutputFileUrl}}" }
            ],
            "error": [
              { "type": "OperationOutcome", "url": "{{TestUrls.ServiceBaseUrlWithSlash}}output/error-1.ndjson" }
            ]
          }
          """;

    public const string PractitionerNdJson =
        """
        {"resourceType":"Practitioner","id":"practitioner-1"}
        {"resourceType":"Practitioner","id":"practitioner-2"}
        """;

    public const string OrganizationNdJson =
        """
        {"resourceType":"Organization","id":"organization-1"}
        """;

    public const string OperationOutcomeJson =
        """
        {
          "resourceType": "OperationOutcome",
          "issue": [
            {
              "severity": "error",
              "code": "processing",
              "details": { "text": "The _typeFilter is not supported" },
              "diagnostics": "Unsupported parameter"
            }
          ]
        }
        """;
}
