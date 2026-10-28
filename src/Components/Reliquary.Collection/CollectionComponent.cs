using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Collection.Api;
using Reliquary.Collection.Data;
using Reliquary.Collection.Exchange;

namespace Reliquary.Collection;

/// <summary>
/// Registers the Collection core component: locations, location entries, movements and inventories.
/// See docs/decisions/0034-collection-model.md.
/// </summary>
public sealed class CollectionComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Collection";

    /// <inheritdoc />
    public IEnumerable<MigrationSet> MigrationSets =>
    [
        new(DatabaseTarget.Users, "users", "collection", typeof(CollectionComponent).Assembly),
        new(DatabaseTarget.Content, "content", "collection", typeof(CollectionComponent).Assembly),
    ];

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CollectionStore>();
        services.AddSingleton<CardResolver>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<CollectionViews>();
        services.AddSingleton<StorageRuleService>();
        services.AddSingleton<DeckTokenService>();
        services.AddSingleton<MovementService>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<LocationService>();
        services.AddSingleton<EntryService>();
        services.AddSingleton<CollectionExchangeService>();
        services.AddSingleton<ICollectionCatalog, CollectionCatalog>();
        services.AddSingleton<IAssembledDeckLocations, AssembledDeckLocations>();
        services.AddSingleton<IPersonalDataProvider, CollectionPersonalData>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        var collection = api.MapGroup("/collection")
            .WithTags("Collection")
            .AddEndpointFilter<CollectionExceptionFilter>()
            .RequireAuthorization(AuthPolicies.User);
        LocationEndpoints.Map(collection);
        EntryEndpoints.Map(collection);
        SettingsEndpoints.Map(collection);
        ViewEndpoints.Map(collection);
        StorageRuleEndpoints.Map(collection);
        MovementEndpoints.Map(collection);
        InventoryEndpoints.Map(collection);
        ExchangeEndpoints.Map(collection);
    }
}
