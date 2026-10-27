using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Decks.Api;
using Reliquary.Decks.Data;

namespace Reliquary.Decks;

/// <summary>
/// Registers the Decks core component: decks, deck lines, folders and tags. Milestone M2 adds the deck versions and the variants.
/// </summary>
public sealed class DecksComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Decks";

    /// <inheritdoc />
    public IEnumerable<MigrationSet> MigrationSets =>
    [
        new(DatabaseTarget.Users, "users", "decks", typeof(DecksComponent).Assembly),
        new(DatabaseTarget.Content, "content", "decks", typeof(DecksComponent).Assembly),
    ];

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DeckStore>();
        services.AddSingleton<DeckService>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api) => DeckEndpoints.Map(api);
}
