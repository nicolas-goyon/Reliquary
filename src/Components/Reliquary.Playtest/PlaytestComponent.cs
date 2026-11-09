using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reliquary.BuildingBlocks;
using Reliquary.Playtest.Api;
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
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<GameStoreOptions>(configuration.GetSection(GameStoreOptions.Section));
        services.Configure<GameOptions>(configuration.GetSection(GameOptions.Section));
        // One store type for the games: the table state is in OnlineGame.Table. Two store types on the same keys would read the JSON of the other type.
        services.AddSingleton<GameStore<OnlineGame>>();
        services.AddSingleton<GameLimits>();
        services.AddSingleton<GameTokens>();
        services.AddSingleton<GameService>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api) => GameEndpoints.Map(api);
}
