using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks;

namespace Reliquary.Cards;

/// <summary>
/// Registers the Cards core component: card reference data, card search and deck analysis.
/// </summary>
public sealed class CardsComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Cards";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
