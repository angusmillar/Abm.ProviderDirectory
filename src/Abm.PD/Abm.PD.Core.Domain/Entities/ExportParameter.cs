namespace Abm.PD.Core.Domain.Entities;

public class ExportParameter
{
    public required string Type { get; set; }

    public required DateTimeOffset? Since { get; set; }

    public required List<string> TypeFilterList { get; set; }
}
