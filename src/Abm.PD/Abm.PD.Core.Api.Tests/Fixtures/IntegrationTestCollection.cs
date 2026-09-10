namespace Abm.PD.Core.Api.Tests.Fixtures;

[CollectionDefinition(nameof(IntegrationTestCollection))]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    // Marker class only. xUnit wires the fixture automatically.
}
