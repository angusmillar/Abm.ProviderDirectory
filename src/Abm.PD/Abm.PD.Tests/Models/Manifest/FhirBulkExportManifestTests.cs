using System.Text.Json;
using Abm.PD.Domain.Models.Manifest;
using Abm.PD.Tests.TestData;

namespace Abm.PD.Tests.Models.Manifest;

/// <summary>
/// The manifest models are deserialised with System.Text.Json's Web defaults, exactly as FhirBulkExporter does,
/// so these tests read the same wire shapes the exporter will meet.
/// </summary>
public class FhirBulkExportManifestTests
{
    private static readonly JsonSerializerOptions ManifestJsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private static FhirBulkExportManifest Deserialize(
        string json)
    {
        return JsonSerializer.Deserialize<FhirBulkExportManifest>(json, ManifestJsonSerializerOptions)
               ?? throw new InvalidOperationException("The manifest deserialized to null.");
    }

    [Fact]
    public void Deserialize_ReadsEveryMemberOfACurrentBallotManifest()
    {
        FhirBulkExportManifest manifest = Deserialize(BulkExportTestData.FullBallotManifest());

        Assert.Equal(
            "http://hl7.org/fhir/uv/bulkdata/StructureDefinition/output-manifest",
            manifest.ManifestType);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.FromHours(10)),
            manifest.TransactionTime);

        Assert.True(manifest.RequiresAccessToken);
        Assert.Equal("application/fhir+ndjson", manifest.OutputFormat);
        Assert.Equal("Patient", manifest.OutputOrganizedBy);
        Assert.Equal("One file per compartment", manifest.OutputOrganizedByDetail);
    }

    [Fact]
    public void Deserialize_ReadsAnOutputFileEntry()
    {
        FhirBulkExportManifest manifest = Deserialize(BulkExportTestData.FullBallotManifest());

        FhirBulkExportManifestOutputFile outputFile = Assert.Single(manifest.Output!);

        Assert.Equal("Practitioner", outputFile.Type);
        Assert.Equal(TestDoubles.TestUrls.PractitionerOutputFileUrl, outputFile.Url);
        Assert.Equal(TestDoubles.TestUrls.OrganizationOutputFileUrl, outputFile.ContinuesInFile);
        Assert.Equal(12L, outputFile.Count);
        Assert.Equal(4096L, outputFile.FileSize);
    }

    [Fact]
    public void Deserialize_KeepsServerSpecificExtensionDataRatherThanDroppingIt()
    {
        FhirBulkExportManifest manifest = Deserialize(BulkExportTestData.FullBallotManifest());

        Assert.NotNull(manifest.Extension);
        Assert.Equal("au", manifest.Extension!["https://example.test/server"]!["region"]!.GetValue<string>());

        FhirBulkExportManifestOutputFile outputFile = Assert.Single(manifest.Output!);
        Assert.NotNull(outputFile.Extension);
        Assert.Equal("value", outputFile.Extension!["https://example.test/custom"]!.GetValue<string>());
    }

    [Fact]
    public void Deserialize_ReadsTheDeletedOutcomeAndLinkMembers()
    {
        FhirBulkExportManifest manifest = Deserialize(BulkExportTestData.FullBallotManifest());

        FhirBulkExportManifestDeletedFile deletedFile = Assert.Single(manifest.Deleted!);
        Assert.Equal("Bundle", deletedFile.Type);
        Assert.Equal(3L, deletedFile.Count);
        Assert.Equal(128L, deletedFile.FileSize);

        FhirBulkExportManifestOutcomeFile outcomeFile = Assert.Single(manifest.Outcome!);
        Assert.Equal("OperationOutcome", outcomeFile.Type);
        Assert.Equal(2L, outcomeFile.Count);
        Assert.Collection(
            outcomeFile.CountSeverity!,
            severityCount =>
            {
                Assert.Equal("warning", severityCount.Code);
                Assert.Equal(1L, severityCount.Count);
            },
            severityCount =>
            {
                Assert.Equal("information", severityCount.Code);
                Assert.Equal(1L, severityCount.Count);
            });

        FhirBulkExportManifestLink link = Assert.Single(manifest.Link!);
        Assert.Equal("next", link.Relation);
    }

    [Fact]
    public void Deserialize_AVersion2ManifestPopulatesErrorRatherThanOutcome()
    {
        //v2.0.0 named the outcome files "error". Both names are carried so either shape still deserializes, and
        //a v2 server's manifest must not silently lose its error files.
        FhirBulkExportManifest manifest = Deserialize(BulkExportTestData.Version2Manifest());

        Assert.Null(manifest.Outcome);
        Assert.NotNull(manifest.Error);
        Assert.Equal("OperationOutcome", Assert.Single(manifest.Error!).Type);
        Assert.Equal($"{TestDoubles.TestUrls.ServiceBaseUrlWithSlash}$export", manifest.Request);
    }

    [Fact]
    public void Deserialize_AnAbsentOutputFormatIsNullSoTheNdJsonDefaultApplies()
    {
        FhirBulkExportManifest manifest = Deserialize(BulkExportTestData.NoOutputMemberManifest());

        Assert.Null(manifest.OutputFormat);
        Assert.Null(manifest.Output);
    }

    [Fact]
    public void Deserialize_AnEmptyOutputArrayIsAnEmptyListNotNull()
    {
        FhirBulkExportManifest manifest = Deserialize(BulkExportTestData.EmptyOutputManifest());

        Assert.NotNull(manifest.Output);
        Assert.Empty(manifest.Output!);
    }

    [Fact]
    public void Deserialize_IgnoresMembersTheModelDoesNotDeclare()
    {
        //A server adding a member outside the specification must not fail the export.
        FhirBulkExportManifest manifest = Deserialize(
            """
            {
              "transactionTime": "2026-08-31T09:00:00+10:00",
              "requiresAccessToken": false,
              "somethingTheSpecificationHasNeverHeardOf": { "a": 1 }
            }
            """);

        Assert.False(manifest.RequiresAccessToken);
    }

    [Fact]
    public void Deserialize_IsCaseInsensitiveBecauseOfTheWebDefaults()
    {
        FhirBulkExportManifest manifest = Deserialize(
            """
            {
              "TransactionTime": "2026-08-31T09:00:00+10:00",
              "RequiresAccessToken": true,
              "OutputFormat": "application/fhir+ndjson"
            }
            """);

        Assert.True(manifest.RequiresAccessToken);
        Assert.Equal("application/fhir+ndjson", manifest.OutputFormat);
    }

    [Fact]
    public void Deserialize_AnOutputFileWithoutAUrlIsRejected()
    {
        //url is 1..1 and is modelled as a required member, so a manifest that omits it is a hard failure rather
        //than an output file the exporter would later dereference as null.
        JsonException exception = Assert.Throws<JsonException>(() => Deserialize(
            """
            {
              "transactionTime": "2026-08-31T09:00:00+10:00",
              "requiresAccessToken": false,
              "output": [ { "type": "Practitioner" } ]
            }
            """));

        Assert.Contains("Url", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_MalformedJsonThrows()
    {
        Assert.Throws<JsonException>(() => Deserialize("{ this is not json"));
    }
}
