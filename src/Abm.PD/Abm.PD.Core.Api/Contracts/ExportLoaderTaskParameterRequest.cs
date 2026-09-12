namespace Abm.PD.Core.Api.Contracts;

public record ExportLoaderTaskParameterRequest(string Type, DateTimeOffset? Since, List<string> TypeFilterList);
