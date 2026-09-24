using Abm.PD.Core.Application.Settings;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application.IdentifiersSystems;

public class IdentifierSystemSupport(IOptions<ProviderDirectorySettings> providerDirectorySettings)
{
    //Local
    public readonly Uri ProviderDirectoryIgBaseUri = providerDirectorySettings.Value.FhirSystemUriSettings.ProviderDirectoryIgBaseUrl;
    
    public readonly Uri ProviderDirectoryTaskCorrelationId = new(
        $"{providerDirectorySettings.Value.FhirSystemUriSettings.ProviderDirectoryIgBaseUrl.ToString().TrimEnd('/')}/id/pd-task-corelation-id");
    
    //National
    public static readonly Uri MedicareNumber = new Uri("http://ns.electronichealth.net.au/id/medicare-number");
    public static readonly Uri MedicareProviderNumber = new Uri("http://ns.electronichealth.net.au/id/medicare-provider-number");
    public static readonly Uri Dva = new Uri("http://ns.electronichealth.net.au/id/dva");
    public static readonly Uri Ihi = new Uri("http://ns.electronichealth.net.au/id/hi/ihi/1.0");
    public static readonly Uri Hpii = new Uri("http://ns.electronichealth.net.au/id/hi/hpii/1.0");
    public static readonly Uri Hpio = new Uri("http://ns.electronichealth.net.au/id/hi/hpio/1.0");
    public static readonly Uri NataAccreditationNumber = new Uri("http://hl7.org.au/id/nata-accreditation");
    public static readonly Uri AustralianBusinessNumber = new Uri("http://hl7.org.au/id/abn");
}