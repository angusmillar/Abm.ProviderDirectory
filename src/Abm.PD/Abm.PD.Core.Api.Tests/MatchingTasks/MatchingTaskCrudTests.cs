using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Tests.MatchingTasks;

public class MatchingTaskCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    // The API serialises TaskStateId/TaskTypeId as their member name (see Program.cs's
    // ConfigureHttpJsonOptions), so responses containing those enums need the matching converter to
    // deserialise here - System.Net.Http.Json otherwise uses JsonSerializerOptions.Default, which only
    // understands the underlying int.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static MatchingTaskRequest NewRequest(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTimeOffset? toStartAtUtc = null)
    {
        return new MatchingTaskRequest(
            Code: code,
            DisplayName: $"Task {code}",
            Description: "Nightly matching run",
            State: state,
            StateReason: null,
            TriggerEvery: TimeSpan.FromHours(24),
            StartAtUtc: toStartAtUtc,
            EndAtUtc: null,
            MaxRunCount: null,
            MetaData: "{}");
    }

    private static MatchingTaskUpdateRequest AsUpdateRequest(MatchingTaskResponse response) => new(
        DisplayName: response.DisplayName,
        Description: response.Description,
        State: response.State,
        StateReason: response.StateReason,
        TriggerEvery: response.TriggerEvery,
        StartAtUtc: response.StartAtUtc,
        EndAtUtc: response.EndAtUtc,
        MaxRunCount: response.MaxRunCount,
        MetaData: response.MetaData);

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedMatchingTask()
    {
        MatchingTaskRequest request = NewRequest(Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/MatchingTask", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        MatchingTaskResponse? created = await response.Content.ReadFromJsonAsync<MatchingTaskResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(request.Code, created!.Code);
        Assert.Equal(request.DisplayName, created.DisplayName);
        Assert.Equal(TaskTypeId.MatchingTask, created.TypeId);
        Assert.Null(created.LastStartUtc);
        Assert.Null(created.LastEndUtc);
        Assert.NotEqual(default, created.CreatedUtc);
        Assert.NotEqual(default, created.UpdatedUtc);
        Assert.Equal(0, created.FailureCount);
        Assert.Equal(0, created.RunCount);
        Assert.Null(created.MaxRunCount);
        Assert.Null(created.LastCorrelationId);
    }

    [Fact]
    public async Task Create_ValidRequest_SerialisesTypeIdAndStateAsEnumNamesNotIntegers()
    {
        MatchingTaskRequest request = NewRequest(Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/MatchingTask", request);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"typeId\":\"MatchingTask\"", body);
        Assert.Contains("\"state\":\"Ready\"", body);
        Assert.DoesNotContain("\"typeId\":2", body);
        Assert.DoesNotContain("\"state\":1", body);
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns400()
    {
        string code = Guid.NewGuid().ToString();
        HttpResponseMessage firstResponse = await HttpClient.PostAsJsonAsync("/MatchingTask", NewRequest(code));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/MatchingTask", NewRequest(code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingMatchingTask_ReturnsMatchingTask()
    {
        MatchingTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/MatchingTask", request);
        MatchingTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<MatchingTaskResponse>(JsonOptions))!;

        MatchingTaskResponse? fetched =
            await HttpClient.GetFromJsonAsync<MatchingTaskResponse>($"/MatchingTask/{created.Id}", JsonOptions);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.Code, fetched.Code);
    }

    [Fact]
    public async Task GetById_NonExistentMatchingTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/MatchingTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedMatchingTask()
    {
        MatchingTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/MatchingTask", request);
        MatchingTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<MatchingTaskResponse>(JsonOptions))!;

        List<MatchingTaskResponse>? all =
            await HttpClient.GetFromJsonAsync<List<MatchingTaskResponse>>("/MatchingTask", JsonOptions);

        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByCode_FindsMatchingMatchingTask()
    {
        string code = Guid.NewGuid().ToString();
        await HttpClient.PostAsJsonAsync("/MatchingTask", NewRequest(code));

        List<MatchingTaskResponse>? results = await HttpClient.GetFromJsonAsync<List<MatchingTaskResponse>>(
            $"/MatchingTask?code={code}", JsonOptions);

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(code, results![0].Code);
    }

    [Fact]
    public async Task Update_ExistingMatchingTask_PersistsChangesAndPreservesLastStart()
    {
        MatchingTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/MatchingTask", request);
        MatchingTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<MatchingTaskResponse>(JsonOptions))!;
        // Postgres timestamptz truncates to microsecond precision, so the in-memory CreatedUtc from
        // the POST response has finer resolution than what a DB round-trip will return - fetch the
        // persisted baseline rather than comparing against it directly.
        MatchingTaskResponse persistedBaseline =
            (await HttpClient.GetFromJsonAsync<MatchingTaskResponse>($"/MatchingTask/{created.Id}", JsonOptions))!;

        MatchingTaskUpdateRequest updateRequest = AsUpdateRequest(created) with
        {
            DisplayName = "Updated Display Name",
            State = TaskStateId.InProgress,
            StateReason = "Running now",
        };
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/MatchingTask/{created.Id}", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        MatchingTaskResponse? updated = await updateResponse.Content.ReadFromJsonAsync<MatchingTaskResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Display Name", updated!.DisplayName);
        Assert.Equal(TaskStateId.InProgress, updated.State);
        Assert.Equal(persistedBaseline.CreatedUtc, updated.CreatedUtc);
        Assert.Null(updated.LastStartUtc);
        Assert.True(updated.UpdatedUtc >= persistedBaseline.UpdatedUtc);
        // Code is not part of the update payload - must survive unchanged.
        Assert.Equal(created.Code, updated.Code);
    }

    [Fact]
    public async Task Update_NonExistentMatchingTask_Returns404()
    {
        MatchingTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/MatchingTask", request);
        MatchingTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<MatchingTaskResponse>(JsonOptions))!;
        MatchingTaskUpdateRequest updateRequest = AsUpdateRequest(created);

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/MatchingTask/999999", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingMatchingTask_Returns204ThenGetByIdReturns404()
    {
        MatchingTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/MatchingTask", request);
        MatchingTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<MatchingTaskResponse>(JsonOptions))!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/MatchingTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/MatchingTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentMatchingTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.DeleteAsync("/MatchingTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
