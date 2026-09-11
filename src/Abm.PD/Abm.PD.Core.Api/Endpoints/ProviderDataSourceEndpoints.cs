using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Api.Endpoints;

public static class ProviderDataSourceEndpoints
{
    public static IEndpointRouteBuilder MapProviderDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/provider-data-sources", GetAll);
        endpoints.MapGet("/provider-data-sources/search", Search);
        endpoints.MapGet("/provider-data-sources/{id:int}", GetById);
        endpoints.MapPost("/provider-data-sources", Create);
        endpoints.MapPut("/provider-data-sources/{id:int}", Update);
        endpoints.MapDelete("/provider-data-sources/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAll(
        IProviderDataSourceRepository providerDataSourceRepository,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await providerDataSourceRepository.GetAllAsync(cancellationToken));
    }

    private static async Task<IResult> Search(
        string? code,
        string? displayName,
        IProviderDataSourceRepository providerDataSourceRepository,
        CancellationToken cancellationToken)
    {
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
        return Results.Created($"/provider-data-sources/{providerDataSource.Id}", providerDataSource);
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
