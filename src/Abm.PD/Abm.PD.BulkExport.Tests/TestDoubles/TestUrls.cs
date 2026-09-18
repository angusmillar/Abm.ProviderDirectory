namespace Abm.PD.BulkExport.Tests.TestDoubles;

/// <summary>
/// The addresses the tests pretend the provider directory lives at.
///
/// ".test" is reserved by RFC 2606 and can never resolve, so if a change ever did let a request escape the
/// <see cref="StubHttpMessageHandler"/>, the test fails on an unresolvable host rather than quietly reaching a
/// real server.
/// </summary>
public static class TestUrls
{
    public const string ServiceBaseUrl = "https://provider-directory.invalid.test/fhir";
    public const string ServiceBaseUrlWithSlash = ServiceBaseUrl + "/";

    /// <summary>
    /// The FhirNavigator repository Code the tests pretend the source provider directory is configured under.
    /// </summary>
    public const string SourceRepositoryCode = "ProviderConnectAustralia";

    /// <summary>
    /// The FhirNavigator repository Code the tests pretend the target provider directory, the server being
    /// loaded, is configured under.
    /// </summary>
    public const string TargetRepositoryCode = "DevelopmentSmileServer";

    public const string JobId = "dca03739-4b96-41dd-bcaf-d6d4299d125c";

    public const string PollStatusUrlWithJobId =
        ServiceBaseUrlWithSlash + "$export-poll-status?_jobId=" + JobId;

    /// <summary>
    /// The address the tests pretend the target provider directory, the server being loaded, lives at.
    /// </summary>
    public const string TargetServiceBaseUrl = "https://target-directory.invalid.test/fhir";

    public const string PractitionerOutputFileUrl = ServiceBaseUrlWithSlash + "output/Practitioner-1.ndjson";
    public const string OrganizationOutputFileUrl = ServiceBaseUrlWithSlash + "output/Organization-1.ndjson";
}
