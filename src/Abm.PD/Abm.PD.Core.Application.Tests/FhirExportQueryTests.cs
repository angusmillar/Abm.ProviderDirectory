using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;

namespace Abm.PD.Core.Application.Tests;

public class FhirExportQueryTests
{
    [Fact]
    public void FromParameter_BuildsOutputFormatTypeAndTypeFilters()
    {
        ExportParameter parameter = new()
        {
            Type = "Practitioner,Organization",
            Since = null,
            TypeFilterList = ["Practitioner?active=true", "Organization?active=true"],
        };

        Parameters parameters = FhirExportQuery.FromParameter(parameter);

        Assert.Equal("application/fhir+ndjson", GetString(parameters, "_outputFormat"));
        Assert.Equal("Practitioner,Organization", GetString(parameters, "_type"));
        Assert.Equal(
            parameter.TypeFilterList,
            parameters.Parameter.Where(p => p.Name == "_typeFilter").Select(p => ((FhirString)p.Value).Value));
        Assert.DoesNotContain(parameters.Parameter, p => p.Name == "_since");
    }

    [Fact]
    public void FromParameter_WithSince_AddsSinceParameter()
    {
        DateTimeOffset since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ExportParameter parameter = new() { Type = "Patient", Since = since, TypeFilterList = [] };

        Parameters parameters = FhirExportQuery.FromParameter(parameter);

        Instant sinceParam = Assert.IsType<Instant>(parameters.Parameter.Single(p => p.Name == "_since").Value);
        Assert.Equal(since, sinceParam.Value);
    }

    private static string GetString(Parameters parameters, string name) =>
        ((FhirString)parameters.Parameter.Single(p => p.Name == name).Value).Value;
}
