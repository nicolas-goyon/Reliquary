using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reliquary.BuildingBlocks;
using Reliquary.Playtest.Api;
using Reliquary.Playtest.Hub;
using Reliquary.Playtest.Sessions;
using StackExchange.Redis;

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
    public void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<TableBroadcaster>();
        services.AddSingleton<ActiveGames>();
        services.AddHostedService(provider => provider.GetRequiredService<ActiveGames>());
        var signalR = services.AddSignalR(options =>
        {
            // An action is small. The largest message is a deck of the lobby, which goes through the REST endpoints.
            options.MaximumReceiveMessageSize = 16 * 1024;
            options.EnableDetailedErrors = false;
        });

        // The Redis backplane sends the group messages to the connections on all the API replicas.
        // Without a connection string (some tests of the host), the hub works on one replica only.
        if (configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            signalR.AddStackExchangeRedis(options =>
            {
                options.Configuration = ConfigurationOptions.Parse(redis);
                options.Configuration.AbortOnConnectFail = false;
                options.Configuration.ChannelPrefix = RedisChannel.Literal(HubChannelPrefix);
            });
        }
    }

    /// <summary>The start of the Redis channels of the backplane of the table hub.</summary>
    public const string HubChannelPrefix = "reliquary:playtest:hub";

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        GameEndpoints.Map(api);

        // WebSockets only: with no other transport and no negotiation, a client can connect to any replica, with no sticky session.
        api.MapHub<TableHub>(TableHub.Path, options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.AllowStatefulReconnects = false;
        });
    }
}
