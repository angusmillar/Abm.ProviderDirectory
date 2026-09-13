using System.Net;
using System.Net.Http.Json;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.DataSources;

public class DataSourceCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedDataSource()
    {
        DataSourceRequest request = new(Guid.NewGuid().ToString(), "Provider Connect Australia");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/DataSource", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        DataSource? created = await response.Content.ReadFromJsonAsync<DataSource>();
        Assert.NotNull(created);
        Assert.Equal(request.Code, created!.Code);
        Assert.Equal(request.DisplayName, created.DisplayName);
    }

    [Fact]
    public async Task GetById_ExistingDataSource_ReturnsMatchingDataSource()
    {
        DataSourceRequest request = new(Guid.NewGuid().ToString(), "Azure Pyro FHIR Server");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/DataSource", request);
        DataSource created = (await createResponse.Content.ReadFromJsonAsync<DataSource>())!;

        DataSource? fetched = await HttpClient.GetFromJsonAsync<DataSource>($"/DataSource/{created.Id}");

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.Code, fetched.Code);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedDataSource()
    {
        DataSourceRequest request = new(Guid.NewGuid().ToString(), "Provider Connect Australia");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/DataSource", request);
        DataSource created = (await createResponse.Content.ReadFromJsonAsync<DataSource>())!;

        List<DataSource>? all = await HttpClient.GetFromJsonAsync<List<DataSource>>("/DataSource");

        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByCodeAndDisplayName_FindsMatchingDataSource()
    {
        string code = Guid.NewGuid().ToString();
        DataSourceRequest request = new(code, "Azure Pyro FHIR Server");
        await HttpClient.PostAsJsonAsync("/DataSource", request);

        List<DataSource>? results = await HttpClient.GetFromJsonAsync<List<DataSource>>(
            $"/DataSource?code={code}&display-name=Azure Pyro FHIR Server");

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(code, results![0].Code);
    }

    [Fact]
    public async Task Update_ExistingDataSource_PersistsChanges()
    {
        DataSourceRequest request = new(Guid.NewGuid().ToString(), "Provider Connect Australia");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/DataSource", request);
        DataSource created = (await createResponse.Content.ReadFromJsonAsync<DataSource>())!;

        DataSourceRequest updateRequest = new(Guid.NewGuid().ToString(), "Provider Connect Australia (Updated)");
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/DataSource/{created.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        DataSource? updated = await updateResponse.Content.ReadFromJsonAsync<DataSource>();
        Assert.Equal(updateRequest.DisplayName, updated!.DisplayName);

        // The PUT response reflects the tracked in-memory entity - fetch it back to prove the
        // change actually persisted to Postgres.
        DataSource? fetched = await HttpClient.GetFromJsonAsync<DataSource>($"/DataSource/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(updateRequest.DisplayName, fetched!.DisplayName);
    }

    [Fact]
    public async Task Delete_ExistingDataSource_Returns204ThenGetByIdReturns404()
    {
        DataSourceRequest request = new(Guid.NewGuid().ToString(), "Azure Pyro FHIR Server");
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/DataSource", request);
        DataSource created = (await createResponse.Content.ReadFromJsonAsync<DataSource>())!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/DataSource/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/DataSource/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentDataSource_Returns404()
    {
        DataSourceRequest updateRequest = new(Guid.NewGuid().ToString(), "Provider Connect Australia");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/DataSource/999999", updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
