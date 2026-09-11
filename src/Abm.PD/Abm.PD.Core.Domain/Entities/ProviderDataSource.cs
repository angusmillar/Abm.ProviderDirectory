namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// An upstream Provider Directory information source entity 
/// </summary>
public class ProviderDataSource
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public required string DisplayName { get; set; }
    
}
