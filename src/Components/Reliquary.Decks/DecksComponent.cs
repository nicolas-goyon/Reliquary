using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Api;
using Reliquary.Decks.AutoTag;
using Reliquary.Decks.Data;
using Reliquary.Decks.Exchange;
using Reliquary.Decks.Exploration;
using Reliquary.Decks.Recommendations;
using Reliquary.Decks.Text;

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
        services.AddSingleton<TagService>();
        services.AddSingleton<FolderService>();
        services.AddSingleton<PublicDeckIndexer>();
        services.AddSingleton<PublicDeckService>();
        services.AddSingleton<DeckTextService>();
        services.AddSingleton<DeckExchangeService>();
        services.AddSingleton<IPersonalDataProvider, DeckPersonalData>();
        services.AddSingleton<AutoTagService>();
    }

    /// <inheritdoc />
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IDomainEventConsumer, PublicDeckConsumer>();

    /// <inheritdoc />
    public void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RecommendationOptions>(configuration.GetSection(RecommendationOptions.Section));
        services.AddHttpClient(RecommanderSource.HttpClientName, (provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<RecommendationOptions>>().Value;
            var version = typeof(DecksComponent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0";
            client.BaseAddress = settings.BaseUrl;
            client.Timeout = settings.Timeout;
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Reliquary", version));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/nicolas-goyon/reliquary)"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        services.AddSingleton<IRecommendationSource, RecommanderSource>();
        services.AddSingleton<IRecommendationCache, RedisRecommendationCache>();
        services.AddSingleton<RecommendationService>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        DeckEndpoints.Map(api);
        TagEndpoints.Map(api);
        FolderEndpoints.Map(api);
        PublicDeckEndpoints.Map(api);
        RecommendationEndpoints.Map(api);
        AutoTagEndpoints.Map(api);
    }
}
