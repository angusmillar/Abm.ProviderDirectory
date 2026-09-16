using Abm.Core.Time;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Abm.PD.Core.Api.Endpoints;

public static class MatchingTaskEndpoints
{
    public static IEndpointRouteBuilder MapMatchingTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/MatchingTask", GetAllOrSearch);
        endpoints.MapGet("/MatchingTask/{id:int}", GetById);
        endpoints.MapPost("/MatchingTask", Create);
        endpoints.MapPut("/MatchingTask/{id:int}", Update);
        endpoints.MapDelete("/MatchingTask/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? code,
        TaskStateId? state,
        [FromQuery(Name = "last-start-from")] DateTime? lastStartFrom,
        [FromQuery(Name = "last-start-to")] DateTime? lastStartTo,
        IMatchingTaskRepository matchingTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MatchingTask> matchingTaskList = code is null && state is null && lastStartFrom is null && lastStartTo is null
            ? await matchingTaskRepository.GetAllAsync(cancellationToken)
            : await matchingTaskRepository.SearchAsync(
                code: code,
                state: state,
                lastStartFrom: lastStartFrom,
                lastStartTo: lastStartTo,
                cancellationToken: cancellationToken);

        return Results.Ok(matchingTaskList.Select(
            x => MatchingTaskResponse.FromEntity(x, dateTimeProvider)));
    }

    private static async Task<IResult> GetById(
        int id,
        IMatchingTaskRepository matchingTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        MatchingTask? matchingTask = await matchingTaskRepository.GetByIdAsync(id, cancellationToken);
        return matchingTask is null
            ? Results.NotFound()
            : Results.Ok(MatchingTaskResponse.FromEntity(matchingTask, dateTimeProvider));
    }

    private static async Task<IResult> Create(
        MatchingTaskRequest request,
        IMatchingTaskRepository matchingTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        // Code carries a unique index at the database level - checking first turns what would
        // otherwise surface as an unhandled DbUpdateException on SaveChangesAsync into a clear 400.
        IReadOnlyList<MatchingTask> existingWithCode = await matchingTaskRepository.SearchAsync(
            code: request.Code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: cancellationToken);
        if (existingWithCode.Count > 0)
        {
            return Results.BadRequest($"MatchingTask with Code '{request.Code}' already exists");
        }

        DateTime nowUtc = DateTime.UtcNow;
        MatchingTask matchingTask = new()
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
        matchingTask = await matchingTaskRepository.AddAsync(matchingTask, cancellationToken);
        return Results.Created(
            $"/MatchingTask/{matchingTask.Id}",
            MatchingTaskResponse.FromEntity(matchingTask, dateTimeProvider));
    }

    private static async Task<IResult> Update(
        int id,
        MatchingTaskUpdateRequest request,
        IMatchingTaskRepository matchingTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        // Code is deliberately absent from MatchingTaskUpdateRequest - it is immutable after
        // creation.
        MatchingTask? existing = await matchingTaskRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.State == TaskStateId.InProgress)
        {
            return Results.BadRequest($"The MatchingTask State={existing.State}', " +
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

        MatchingTask? updated = await matchingTaskRepository.UpdateAsync(id, existing, cancellationToken);
        return updated is null
            ? Results.NotFound()
            : Results.Ok(MatchingTaskResponse.FromEntity(updated, dateTimeProvider));
    }

    private static async Task<IResult> Delete(
        int id,
        IMatchingTaskRepository matchingTaskRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await matchingTaskRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
