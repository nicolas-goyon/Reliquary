using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Events;
using Reliquary.BuildingBlocks.Versioning;

namespace Reliquary.Platform.InternalApi;

/// <summary>The version of the internal API.</summary>
/// <param name="CoreApi">The current version of the core API, in the Semantic Versioning format. The major version is the version in the path.</param>
/// <param name="Path">The base path of this version of the internal API.</param>
public sealed record InternalApiVersionResponse(string CoreApi, string Path);

/// <summary>The domain event types that modules can consume.</summary>
/// <param name="Items">The event types, sorted by name, then by version.</param>
public sealed record DomainEventTypeListResponse(IReadOnlyList<DomainEventType> Items);

/// <summary>
/// The endpoints of the internal API that the platform gives: the version and the catalog of the domain events.
/// </summary>
public static class InternalApiEndpoints
{
    /// <summary>
    /// Adds the endpoints of the platform to the internal API.
    /// </summary>
    /// <param name="internalApi">The route group of the internal API.</param>
    public static void MapPlatformInternalEndpoints(this IEndpointRouteBuilder internalApi)
    {
        internalApi.MapGet(string.Empty, GetVersion)
            .WithTags("Internal API")
            .WithName("GetInternalApiVersion")
            .WithSummary("Gets the version of the core API. A module checks it before it uses the internal API.");

        internalApi.MapGet("/events/types", ListEventTypes)
            .WithTags("Domain events")
            .WithName("ListDomainEventTypes")
            .WithSummary("Lists the domain event types and versions that modules can consume, with the fields of each payload.");
    }

    private static Ok<InternalApiVersionResponse> GetVersion() =>
        TypedResults.Ok(new InternalApiVersionResponse(CoreApi.Version.ToString(), ApiRoutes.InternalBase));

    private static Ok<DomainEventTypeListResponse> ListEventTypes(DomainEventCatalog catalog) =>
        TypedResults.Ok(new DomainEventTypeListResponse(catalog.Types));
}
