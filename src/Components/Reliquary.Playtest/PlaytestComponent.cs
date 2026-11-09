using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Sessions;

namespace Reliquary.Playtest;

/// <summary>
/// Registers the Playtest core component: the table engine and the game sessions of online games.
/// </summary>
public sealed class PlaytestComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Playtest";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GameStoreOptions>(configuration.GetSection(GameStoreOptions.Section));
        services.AddSingleton<GameStore<TableState>>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
