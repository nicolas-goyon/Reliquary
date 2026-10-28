using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Platform;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.InternalApi;

namespace Reliquary.Hosting;

/// <summary>
/// Registers the platform and the core components in a host.
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// Adds the platform and the services of each core component. All hosts call this method.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddReliquaryCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReliquaryPlatform(configuration);
        services.AddSingleton(DomainEventCatalog.FromAssemblies(CoreComponents.All.Select(component => component.GetType().Assembly)));

        foreach (var component in CoreComponents.All)
        {
            foreach (var stream in component.MigrationSets)
            {
                services.AddSingleton(stream);
            }

            component.AddServices(services, configuration);
        }

        return services;
    }

    /// <summary>
    /// Adds the services that only the API host uses, for each core component.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddReliquaryApi(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var component in CoreComponents.All)
        {
            component.AddApiServices(services, configuration);
        }

        return services;
    }

    /// <summary>
    /// Adds the background services of the worker process and the worker services of each core component.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddReliquaryWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReliquaryWorkerPlatform();

        foreach (var component in CoreComponents.All)
        {
            component.AddWorkerServices(services, configuration);
        }

        return services;
    }

    /// <summary>
    /// Adds the endpoints of each core component to the public API.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapReliquaryEndpoints(this RouteGroupBuilder api)
    {
        foreach (var component in CoreComponents.All)
        {
            component.ConfigureApi(api);
        }

        foreach (var component in CoreComponents.All)
        {
            component.MapEndpoints(api);
        }

        api.MapExtensionEndpoints();

        return api;
    }

    /// <summary>
    /// Adds the endpoints of the platform and of each core component to the internal API for the modules.
    /// </summary>
    /// <param name="internalApi">The route group of the internal API.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapReliquaryInternalEndpoints(this RouteGroupBuilder internalApi)
    {
        internalApi.MapPlatformInternalEndpoints();

        foreach (var component in CoreComponents.All)
        {
            component.MapInternalEndpoints(internalApi);
        }

        return internalApi;
    }
}
