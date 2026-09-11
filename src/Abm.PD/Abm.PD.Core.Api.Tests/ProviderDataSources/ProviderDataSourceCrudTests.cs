using System.Net;
using System.Net.Http.Json;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.ProviderDataSources;

public class ProviderDataSourceCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedProviderDataSource()
    {
        ProviderDataSourceRequest request = new(Guid.NewGuid().ToString(), "Provider Connect Australia");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/provider-data-sources", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ProviderDataSource? created = await response.Content.ReadFromJsonAsync<ProviderDataSource>();
        Assert.NotNull(created);
        Assert.Equal(request.Code, created!.Code);
        Assert.Equal(request.DisplayName, created.DisplayName);
    }

    [Fact]
    public async Task GetById_ExistingProviderDataSource_ReturnsMatchingProviderDataSource()
    {
        ProviderDataSourceRequest request = new(Guid.NewGuid().ToString(), "Azure Pyro FHIR Server");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/provider-data-sources", request);
        ProviderDataSource created = (await createResponse.Content.ReadFromJsonAsync<ProviderDataSource>())!;

        ProviderDataSource? fetched = await HttpClient.GetFromJsonAsync<ProviderDataSource>($"/provider-data-sources/{created.Id}");

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.Code, fetched.Code);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedProviderDataSource()
    {
        ProviderDataSourceRequest request = new(Guid.NewGuid().ToString(), "Provider Connect Australia");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/provider-data-sources", request);
        ProviderDataSource created = (await createResponse.Content.ReadFromJsonAsync<ProviderDataSource>())!;

        List<ProviderDataSource>? all = await HttpClient.GetFromJsonAsync<List<ProviderDataSource>>("/provider-data-sources");

        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByCodeAndDisplayName_FindsMatchingProviderDataSource()
    {
        string code = Guid.NewGuid().ToString();
        ProviderDataSourceRequest request = new(code, "Azure Pyro FHIR Server");
        await HttpClient.PostAsJsonAsync("/provider-data-sources", request);

        List<ProviderDataSource>? results = await HttpClient.GetFromJsonAsync<List<ProviderDataSource>>(
            $"/provider-data-sources/search?code={code}&displayName=Azure Pyro FHIR Server");

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(code, results![0].Code);
    }

    [Fact]
    public async Task Update_ExistingProviderDataSource_PersistsChanges()
    {
        ProviderDataSourceRequest request = new(Guid.NewGuid().ToString(), "Provider Connect Australia");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/provider-data-sources", request);
        ProviderDataSource created = (await createResponse.Content.ReadFromJsonAsync<ProviderDataSource>())!;

        ProviderDataSourceRequest updateRequest = new(Guid.NewGuid().ToString(), "Provider Connect Australia (Updated)");
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/provider-data-sources/{created.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        ProviderDataSource? updated = await updateResponse.Content.ReadFromJsonAsync<ProviderDataSource>();
        Assert.Equal(updateRequest.DisplayName, updated!.DisplayName);

        // The PUT response reflects the tracked in-memory entity - fetch it back to prove the
        // change actually persisted to Postgres.
        ProviderDataSource? fetched = await HttpClient.GetFromJsonAsync<ProviderDataSource>($"/provider-data-sources/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(updateRequest.DisplayName, fetched!.DisplayName);
    }

    [Fact]
    public async Task Delete_ExistingProviderDataSource_Returns204ThenGetByIdReturns404()
    {
        ProviderDataSourceRequest request = new(Guid.NewGuid().ToString(), "Azure Pyro FHIR Server");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/provider-data-sources", request);
        ProviderDataSource created = (await createResponse.Content.ReadFromJsonAsync<ProviderDataSource>())!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/provider-data-sources/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/provider-data-sources/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentProviderDataSource_Returns404()
    {
        ProviderDataSourceRequest updateRequest = new(Guid.NewGuid().ToString(), "Provider Connect Australia");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/provider-data-sources/999999", updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
