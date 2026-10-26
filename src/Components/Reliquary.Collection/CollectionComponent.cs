using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks;

namespace Reliquary.Collection;

/// <summary>
/// Registers the Collection core component: locations, location entries, movements and inventories.
/// </summary>
public sealed class CollectionComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Collection";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
