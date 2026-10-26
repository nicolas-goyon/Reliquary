using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.BuildingBlocks;

/// <summary>
/// An internal part of the core, for example Decks or Collection.
/// </summary>
/// <remarks>
/// A core component owns its tables.
/// It communicates with other core components only through interfaces and domain events.
/// The architecture tests stop a reference from one core component to another.
/// See docs/architecture/overview.md.
/// </remarks>
public interface ICoreComponent
{
    /// <summary>
    /// Gets the name of the core component, for example "Decks".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the migration sets of the core component: the SQL scripts that create and change its tables.
    /// </summary>
    IEnumerable<MigrationSet> MigrationSets => [];

    /// <summary>
    /// Adds the services of the core component to the service collection of each host.
    /// </summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">The configuration of the host.</param>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Adds the services that only the API host uses, for example the authentication and the endpoint filters.
    /// </summary>
    /// <param name="services">The service collection of the API host.</param>
    /// <param name="configuration">The configuration of the API host.</param>
    void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>
    /// Adds the services that only the worker process uses, for example scheduled jobs and event consumers.
    /// </summary>
    /// <param name="services">The service collection of the worker.</param>
    /// <param name="configuration">The configuration of the worker.</param>
    void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>
    /// Adds conventions to all the endpoints of the public API, for example an endpoint filter.
    /// The host calls this method on each core component before it maps the endpoints.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    void ConfigureApi(RouteGroupBuilder api)
    {
    }

    /// <summary>
    /// Adds the endpoints of the core component to the public API.
    /// </summary>
    /// <param name="api">The route group of the public API, for example <c>/api/v1</c>.</param>
    void MapEndpoints(IEndpointRouteBuilder api);
}
