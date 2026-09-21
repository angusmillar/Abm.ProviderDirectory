using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.SeedDirectoryTasks;

public class SeedDirectoryTaskRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static SeedDirectoryTask NewTask(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null,
        DateTime? toStartAtUtc = null,
        DateTime? toEndAtUtc = null,
        int failureCount = 0)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new SeedDirectoryTask
        {
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = triggerEvery ?? TimeSpan.FromHours(24),
            StartAtUtc = toStartAtUtc,
            EndAtUtc = toEndAtUtc,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStartUtc = lastStart,
            LastEndUtc = null,
            FailureCount = failureCount,
            MetaData = "{}",
        };
    }

    [Fact]
    public async Task AddAsync_NewTask_PersistsAndReturnsWithId()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();

        SeedDirectoryTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.True(added.Id > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingTask_ReturnsSeedDirectoryTaskWithTypeId()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();
        SeedDirectoryTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        SeedDirectoryTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(added.Code, fetched!.Code);
        Assert.Equal(TaskTypeId.SeedDirectoryTask, fetched.TypeId);
    }

    [Fact]
    public async Task GetAllAsync_AfterAdd_ContainsAddedTask()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();
        SeedDirectoryTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<SeedDirectoryTask> all = await repository.GetAllAsync(CancellationToken.None);

        Assert.Contains(all, x => x.Id == added.Id);
    }

    [Fact]
    public async Task UpdateAsync_ExistingTask_PersistsChanges()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();
        SeedDirectoryTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        SeedDirectoryTask update = NewTask(added.Code, TaskStateId.InProgress);
        // A fixed value clear of DateTime.UtcNow's sub-microsecond precision, so it round-trips
        // through the timestamptz column (microsecond precision) without truncation flakiness.
        DateTime newUpdatedUtc = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        update.UpdatedUtc = newUpdatedUtc;
        SeedDirectoryTask? updated = await repository.UpdateAsync(added.Id, update, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.InProgress, updated!.State);

        SeedDirectoryTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(newUpdatedUtc, fetched.UpdatedUtc);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentTask_ReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();

        SeedDirectoryTask? updated = await repository.UpdateAsync(999999, NewTask("missing"), CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_ExistingTask_RemovesItThenGetByIdReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();
        SeedDirectoryTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        bool deleted = await repository.DeleteAsync(added.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(added.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_NonExistentTask_ReturnsFalse()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();

        bool deleted = await repository.DeleteAsync(999999, CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task SearchAsync_ByCode_FindsSeedDirectoryTask()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();
        string code = Guid.NewGuid().ToString();
        await repository.AddAsync(NewTask(code), CancellationToken.None);

        IReadOnlyList<SeedDirectoryTask> results = await repository.SearchAsync(
            code: code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(code, results[0].Code);
    }

    [Fact]
    public async Task SearchAsync_ByState_FindsOnlyMatchingState()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        ISeedDirectoryTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<ISeedDirectoryTaskRepository>();
        await repository.AddAsync(NewTask(Guid.NewGuid().ToString(), TaskStateId.Ready), CancellationToken.None);
        SeedDirectoryTask inProgress = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), TaskStateId.InProgress), CancellationToken.None);

        IReadOnlyList<SeedDirectoryTask> results = await repository.SearchAsync(
            code: null,
            state: TaskStateId.InProgress,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Contains(results, x => x.Id == inProgress.Id);
        Assert.All(results, x => Assert.Equal(TaskStateId.InProgress, x.State));
    }
}
