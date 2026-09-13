using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Abm.PD.Core.Api.Endpoints;

public static class DataSourceEndpoints
{
    public static IEndpointRouteBuilder MapDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/DataSource", GetAllOrSearch);
        endpoints.MapGet("/DataSource/{id:int}", GetById);
        endpoints.MapPost("/DataSource", Create);
        endpoints.MapPut("/DataSource/{id:int}", Update);
        endpoints.MapDelete("/DataSource/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? code,
        [FromQuery(Name = "display-name")] string? displayName,
        IDataSourceRepository dataSourceRepository,
        CancellationToken cancellationToken)
    {
        if (code is null && displayName is null)
        {
            return Results.Ok(await dataSourceRepository.GetAllAsync(cancellationToken));
        }

        return Results.Ok(await dataSourceRepository.SearchAsync(code, displayName, cancellationToken));
    }

    private static async Task<IResult> GetById(
        int id,
        IDataSourceRepository dataSourceRepository,
        CancellationToken cancellationToken)
    {
        DataSource? dataSource = await dataSourceRepository.GetByIdAsync(id, cancellationToken);
        return dataSource is null
            ? Results.NotFound()
            : Results.Ok(dataSource);
    }

    private static async Task<IResult> Create(
        DataSourceRequest request,
        IDataSourceRepository dataSourceRepository,
        CancellationToken cancellationToken)
    {
        DataSource dataSource = new()
        {
            Code = request.Code,
            DisplayName = request.DisplayName,
        };
        dataSource = await dataSourceRepository.AddAsync(dataSource, cancellationToken);
        return Results.Created($"/DataSource/{dataSource.Id}", dataSource);
    }

    private static async Task<IResult> Update(
        int id,
        DataSourceRequest request,
        IDataSourceRepository dataSourceRepository,
        CancellationToken cancellationToken)
    {
        DataSource? dataSource = await dataSourceRepository.UpdateAsync(
            id: id,
            code: request.Code,
            displayName: request.DisplayName,
            cancellationToken: cancellationToken);
        return dataSource is null
            ? Results.NotFound()
            : Results.Ok(dataSource);
    }

    private static async Task<IResult> Delete(
        int id,
        IDataSourceRepository dataSourceRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await dataSourceRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
