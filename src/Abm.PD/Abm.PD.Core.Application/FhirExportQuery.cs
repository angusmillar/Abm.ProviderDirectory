using Hl7.Fhir.Model;

namespace Abm.PD.Core.Application;

public static class FhirExportQuery
{
    public static Parameters GetByPostCode()
    {
        Parameters parameters = new Parameters();
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_outputFormat",
            Value = new FhirString("application/fhir+ndjson")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_type",
            Value = new FhirString("Location,HealthcareService,Organization,PractitionerRole,Practitioner")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString("Location?address-city=Balmain&address-postalcode=2041&near=-33.8607|151.1803|100")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString(
                "HealthcareService?service-type=http://snomed.info/sct|789718008&location.address-city=Balmain&location.address-postalcode=2041")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString(
                "Organization?_has:HealthcareService:organization:service-type=http://snomed.info/sct|789718008")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString(
                "PractitionerRole?location.address-city=Balmain&location.address-postalcode=2041")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString("Practitioner?_has:PractitionerRole:practitioner:location.address-city=Balmain")
        });

        return parameters;
    }

    public static Parameters GetEndpoints(
        DateTimeOffset fromDateTime)
    {
        Parameters parameters = new Parameters();
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_outputFormat",
            Value = new FhirString("application/fhir+ndjson")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_since",
            Value = new Instant() { Value = fromDateTime }
        });

        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_type",
            Value = new FhirString("Endpoint")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString("Endpoint?_lastUpdated=gt2010")
        });

        return parameters;
    }

    public static Parameters GetEveryThingFrom(
        DateTimeOffset fromDateTime)
    {
        var since = new Instant() { Value = fromDateTime };

        Parameters parameters = new Parameters();
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_outputFormat",
            Value = new FhirString("application/fhir+ndjson")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_since",
            Value = since
        });

        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_type",
            Value = new FhirString("Organization,Location,Endpoint,Practitioner,HealthcareService,PractitionerRole")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Organization?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Location?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Endpoint?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Practitioner?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"HealthcareService?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"PractitionerRole?_lastUpdated=ge{since}")
        });

        return parameters;
    }
}