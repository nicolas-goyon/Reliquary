using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks;

namespace Reliquary.Decks;

/// <summary>
/// Registers the Decks core component: decks, deck versions, variants, tags and folders.
/// </summary>
public sealed class DecksComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Decks";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
