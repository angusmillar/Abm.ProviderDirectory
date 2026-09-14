namespace Abm.PD.Core.Api.Contracts;

public record ExportTaskParameterRequest(string Type, DateTimeOffset? Since, List<string> TypeFilterList);
