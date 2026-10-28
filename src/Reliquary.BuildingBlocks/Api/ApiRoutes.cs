namespace Reliquary.BuildingBlocks.Api;

/// <summary>
/// The stable paths of the public API and of the internal API.
/// </summary>
public static class ApiRoutes
{
    /// <summary>The path of the version 1 of the public API.</summary>
    public const string Base = "/api/v1";

    /// <summary>The path of the version 1 of the internal API for the modules. Only service accounts can use it.</summary>
    public const string InternalBase = "/internal/v1";

    /// <summary>The name of the OpenAPI document of the internal API. The API serves it at <c>/openapi/internal-v1.json</c>.</summary>
    public const string InternalDocument = "internal-v1";
}
