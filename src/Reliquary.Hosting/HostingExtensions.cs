using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform;

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
    public static IEndpointRouteBuilder MapReliquaryEndpoints(this IEndpointRouteBuilder api)
    {
        foreach (var component in CoreComponents.All)
        {
            component.MapEndpoints(api);
        }

        return api;
    }
}
