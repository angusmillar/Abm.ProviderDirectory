using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Abm.PD.Core.Api.Endpoints;

public static class ProviderDataSourceEndpoints
{
    public static IEndpointRouteBuilder MapProviderDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ProviderDataSource", GetAllOrSearch);
        endpoints.MapGet("/ProviderDataSource/{id:int}", GetById);
        endpoints.MapPost("/ProviderDataSource", Create);
        endpoints.MapPut("/ProviderDataSource/{id:int}", Update);
        endpoints.MapDelete("/ProviderDataSource/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? code,
        [FromQuery(Name = "display-name")] string? displayName,
        IProviderDataSourceRepository providerDataSourceRepository,
        CancellationToken cancellationToken)
    {
        if (code is null && displayName is null)
        {
            return Results.Ok(await providerDataSourceRepository.GetAllAsync(cancellationToken));
        }

        return Results.Ok(await providerDataSourceRepository.SearchAsync(code, displayName, cancellationToken));
    }

    private static async Task<IResult> GetById(
        int id,
        IProviderDataSourceRepository providerDataSourceRepository,
        CancellationToken cancellationToken)
    {
        ProviderDataSource? providerDataSource = await providerDataSourceRepository.GetByIdAsync(id, cancellationToken);
        return providerDataSource is null
            ? Results.NotFound()
            : Results.Ok(providerDataSource);
    }

    private static async Task<IResult> Create(
        ProviderDataSourceRequest request,
        IProviderDataSourceRepository providerDataSourceRepository,
        CancellationToken cancellationToken)
    {
        ProviderDataSource providerDataSource = new()
        {
            Code = request.Code,
            DisplayName = request.DisplayName,
        };
        providerDataSource = await providerDataSourceRepository.AddAsync(providerDataSource, cancellationToken);
        return Results.Created($"/ProviderDataSource/{providerDataSource.Id}", providerDataSource);
    }

    private static async Task<IResult> Update(
        int id,
        ProviderDataSourceRequest request,
        IProviderDataSourceRepository providerDataSourceRepository,
        CancellationToken cancellationToken)
    {
        ProviderDataSource? providerDataSource = await providerDataSourceRepository.UpdateAsync(
            id: id,
            code: request.Code,
            displayName: request.DisplayName,
            cancellationToken: cancellationToken);
        return providerDataSource is null
            ? Results.NotFound()
            : Results.Ok(providerDataSource);
    }

    private static async Task<IResult> Delete(
        int id,
        IProviderDataSourceRepository providerDataSourceRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await providerDataSourceRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
