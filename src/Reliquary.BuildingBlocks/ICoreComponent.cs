using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    /// Adds the services of the core component to the service collection of the host.
    /// </summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">The configuration of the host.</param>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Adds the endpoints of the core component to the public API.
    /// </summary>
    /// <param name="api">The route group of the public API, for example <c>/api/v1</c>.</param>
    void MapEndpoints(IEndpointRouteBuilder api);
}
