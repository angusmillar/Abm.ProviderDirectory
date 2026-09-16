using Abm.PD.Core.Application.MatchingTaskRunner;
using Abm.PD.Core.Application.Tests.Loader.TestDoubles;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Abm.PD.Core.Application.Tests;

/// <summary>
/// Covers MatchingTaskRunner reading source_resource rows for the task's target CorrelationId (carried as the
/// raw Guid in MatchingTask.MetaData, distinct from the run's own correlationId) and deserialising each into a
/// Firely POCO.
/// </summary>
public class MatchingTaskRunnerTests
{
    private static readonly Guid TargetCorrelationId = Guid.Parse("018f6e6e-0000-7000-8000-000000000010");
    private static readonly Guid RunCorrelationId = Guid.Parse("018f6e6e-0000-7000-8000-000000000099");

    private static readonly string[] ExpectedResourceTypes =
    [
        "Practitioner", "Endpoint", "Organization", "Location", "HealthcareService", "PractitionerRole",
    ];

    private static MatchingTask NewMatchingTask(
        Guid? targetCorrelationId = null)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new MatchingTask
        {
            Code = Guid.NewGuid().ToString(),
            DisplayName = "Test Matching Task",
            State = TaskStateId.InProgress,
            StateReason = null,
            TriggerEvery = TimeSpan.FromHours(24),
            StartAtUtc = null,
            EndAtUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStartUtc = null,
            LastEndUtc = null,
            MetaData = (targetCorrelationId ?? TargetCorrelationId).ToString(),
        };
    }

    private static int _nextSourceResourceId = 1;

    private static SourceResource NewSourceResource(
        Guid correlationId,
        string resourceType,
        string resourceId,
        string json)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DataSource dataSource = new() { Id = 1, Code = "test-source", DisplayName = "Test Source" };
        return new SourceResource
        {
            // A real row's Id is a unique DB-assigned primary key; FakeSourceResourceRepository.UpdateResourceAsync
            // looks a seeded resource up by Id the same way, so every fixture needs a distinct one.
            Id = _nextSourceResourceId++,
            CorrelationId = correlationId,
            ResourceType = resourceType,
            SourceResourceId = resourceId,
            TargetResourceId = Guid.CreateVersion7(),
            ResourceLastUpdated = DateTimeOffset.UtcNow,
            DataSourceId = dataSource.Id,
            DataSource = dataSource,
            Resource = json,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    private static MatchingTaskRunner.MatchingTaskRunner NewRunner(
        FakeSourceResourceRepository repository)
    {
        return new MatchingTaskRunner.MatchingTaskRunner(
            NullLogger<MatchingTaskRunner.MatchingTaskRunner>.Instance,
            repository);
    }

    [Fact]
    public async Task Run_QueriesAllSixResourceTypes_UsingTheCorrelationIdFromMetaData_NotTheRunsOwnCorrelationId()
    {
        FakeSourceResourceRepository repository = new();
        MatchingTaskRunner.MatchingTaskRunner runner = NewRunner(repository);
        MatchingTask matchingTask = NewMatchingTask();

        await runner.Run(matchingTask, RunCorrelationId, CancellationToken.None);

        Assert.Equal(
            ExpectedResourceTypes.OrderBy(x => x),
            repository.ReceivedGetByCorrelationIdCalls.Select(x => x.ResourceType).OrderBy(x => x));
        Assert.All(repository.ReceivedGetByCorrelationIdCalls, call => Assert.Equal(TargetCorrelationId, call.CorrelationId));
    }

    [Fact]
    public async Task Run_DeserialisesEachSourceResourceJsonIntoAFirelyPoco_AndCountsIt()
    {
        FakeSourceResourceRepository repository = new();
        repository.SeededResources.Add(NewSourceResource(
            TargetCorrelationId, "Practitioner", "1", "{\"resourceType\":\"Practitioner\",\"id\":\"1\"}"));
        repository.SeededResources.Add(NewSourceResource(
            TargetCorrelationId, "Endpoint", "2", "{\"resourceType\":\"Endpoint\",\"id\":\"2\",\"status\":\"active\",\"connectionType\":{\"code\":\"hl7-fhir-rest\"},\"payloadType\":[{\"coding\":[{\"code\":\"any\"}]}],\"address\":\"https://example.test\"}"));
        MatchingTaskRunner.MatchingTaskRunner runner = NewRunner(repository);

        MatchingTaskResult result = await runner.Run(NewMatchingTask(), RunCorrelationId, CancellationToken.None);

        Assert.Equal(2, result.ProcessedCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task Run_ASourceResourceWithInvalidJson_IsCountedAsFailed_AndProcessingContinues()
    {
        FakeSourceResourceRepository repository = new();
        repository.SeededResources.Add(NewSourceResource(
            TargetCorrelationId, "Practitioner", "1", "not-valid-json"));
        repository.SeededResources.Add(NewSourceResource(
            TargetCorrelationId, "Practitioner", "2", "{\"resourceType\":\"Practitioner\",\"id\":\"2\"}"));
        MatchingTaskRunner.MatchingTaskRunner runner = NewRunner(repository);

        MatchingTaskResult result = await runner.Run(NewMatchingTask(), RunCorrelationId, CancellationToken.None);

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.FailedCount);
    }
}
