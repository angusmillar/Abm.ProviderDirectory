using Abm.Core.Time;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Abm.PD.Core.Api.Endpoints;

public static class SeedDirectoryTaskEndpoints
{
    public static IEndpointRouteBuilder MapSeedDirectoryTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/SeedDirectoryTask", GetAllOrSearch);
        endpoints.MapGet("/SeedDirectoryTask/{id:int}", GetById);
        endpoints.MapPost("/SeedDirectoryTask", Create);
        endpoints.MapPut("/SeedDirectoryTask/{id:int}", Update);
        endpoints.MapDelete("/SeedDirectoryTask/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? code,
        TaskStateId? state,
        [FromQuery(Name = "last-start-from")] DateTime? lastStartFrom,
        [FromQuery(Name = "last-start-to")] DateTime? lastStartTo,
        ISeedDirectoryTaskRepository seedDirectoryTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SeedDirectoryTask> seedDirectoryTaskList = code is null && state is null && lastStartFrom is null && lastStartTo is null
            ? await seedDirectoryTaskRepository.GetAllAsync(cancellationToken)
            : await seedDirectoryTaskRepository.SearchAsync(
                code: code,
                state: state,
                lastStartFrom: lastStartFrom,
                lastStartTo: lastStartTo,
                cancellationToken: cancellationToken);

        return Results.Ok(seedDirectoryTaskList.Select(
            x => SeedDirectoryTaskResponse.FromEntity(x, dateTimeProvider)));
    }

    private static async Task<IResult> GetById(
        int id,
        ISeedDirectoryTaskRepository seedDirectoryTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        SeedDirectoryTask? seedDirectoryTask = await seedDirectoryTaskRepository.GetByIdAsync(id, cancellationToken);
        return seedDirectoryTask is null
            ? Results.NotFound()
            : Results.Ok(SeedDirectoryTaskResponse.FromEntity(seedDirectoryTask, dateTimeProvider));
    }

    private static async Task<IResult> Create(
        SeedDirectoryTaskRequest request,
        ISeedDirectoryTaskRepository seedDirectoryTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        // Code carries a unique index at the database level - checking first turns what would
        // otherwise surface as an unhandled DbUpdateException on SaveChangesAsync into a clear 400.
        IReadOnlyList<SeedDirectoryTask> existingWithCode = await seedDirectoryTaskRepository.SearchAsync(
            code: request.Code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: cancellationToken);
        if (existingWithCode.Count > 0)
        {
            return Results.BadRequest($"SeedDirectoryTask with Code '{request.Code}' already exists");
        }

        DateTime nowUtc = DateTime.UtcNow;
        SeedDirectoryTask seedDirectoryTask = new()
        {
            Code = request.Code,
            DisplayName = request.DisplayName,
            Description = request.Description,
            State = request.State,
            StateReason = request.StateReason,
            TriggerEvery = request.TriggerEvery,
            // DateTimeOffset.UtcDateTime always yields Kind=Utc regardless of the offset the caller
            // sent - Npgsql rejects a DateTime with Kind=Local or Unspecified for a "timestamp with
            // time zone" column, which a plain DateTime? here could otherwise carry depending on how
            // the incoming JSON's offset was parsed.
            StartAtUtc = request.StartAtUtc?.UtcDateTime,
            EndAtUtc = request.EndAtUtc?.UtcDateTime,
            MaxRunCount = request.MaxRunCount,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStartUtc = null,
            LastEndUtc = null,
            MetaData = request.MetaData,
        };
        seedDirectoryTask = await seedDirectoryTaskRepository.AddAsync(seedDirectoryTask, cancellationToken);
        return Results.Created(
            $"/SeedDirectoryTask/{seedDirectoryTask.Id}",
            SeedDirectoryTaskResponse.FromEntity(seedDirectoryTask, dateTimeProvider));
    }

    private static async Task<IResult> Update(
        int id,
        SeedDirectoryTaskUpdateRequest request,
        ISeedDirectoryTaskRepository seedDirectoryTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        // Code is deliberately absent from SeedDirectoryTaskUpdateRequest - it is immutable after
        // creation.
        SeedDirectoryTask? existing = await seedDirectoryTaskRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.State == TaskStateId.InProgress)
        {
            return Results.BadRequest($"The SeedDirectoryTask State={existing.State}', " +
                                      $"can not modify a task while {nameof(TaskStateId.InProgress)} ");
        }

        existing.DisplayName = request.DisplayName;
        existing.Description = request.Description;
        existing.State = request.State;
        existing.StateReason = request.StateReason;
        existing.TriggerEvery = request.TriggerEvery;
        existing.StartAtUtc = request.StartAtUtc?.UtcDateTime;
        existing.EndAtUtc = request.EndAtUtc?.UtcDateTime;
        existing.MaxRunCount = request.MaxRunCount;
        existing.UpdatedUtc = DateTime.UtcNow;
        existing.MetaData = request.MetaData;

        SeedDirectoryTask? updated = await seedDirectoryTaskRepository.UpdateAsync(id, existing, cancellationToken);
        return updated is null
            ? Results.NotFound()
            : Results.Ok(SeedDirectoryTaskResponse.FromEntity(updated, dateTimeProvider));
    }

    private static async Task<IResult> Delete(
        int id,
        ISeedDirectoryTaskRepository seedDirectoryTaskRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await seedDirectoryTaskRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
