using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Cards.Api;
using Reliquary.Cards.Import;

namespace Reliquary.Cards;

/// <summary>
/// Registers the Cards core component: card reference data, card search and deck analysis.
/// </summary>
public sealed class CardsComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Cards";

    /// <inheritdoc />
    public IEnumerable<MigrationSet> MigrationSets =>
    [
        new(DatabaseTarget.Reference, "reference", "cards", typeof(CardsComponent).Assembly),
    ];

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<CardQueries>();
    }

    /// <inheritdoc />
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CardImportOptions>(configuration.GetSection(CardImportOptions.Section));
        services.AddHttpClient(CardDataImport.HttpClientName, client =>
        {
            // Scryfall asks each client for a User-Agent and an Accept header.
            var version = typeof(CardsComponent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Reliquary", version));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/nicolas-goyon/reliquary)"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
            client.Timeout = TimeSpan.FromMinutes(30);
        });
        services.AddSingleton<CardFileImporter>();
        services.AddSingleton<ReferenceImporters>();
        services.AddSingleton<CardDataImport>();

        if (configuration.GetValue($"{CardImportOptions.Section}:Enabled", true))
        {
            services.AddSingleton<IScheduledJob, CardImportJob>();
        }
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api) => CardEndpoints.Map(api);
}
