using System.Security.Claims;
using Microsoft.AspNetCore.Builder;

namespace Reliquary.BuildingBlocks.Security;

/// <summary>
/// Marks an endpoint that accepts only a plugin token, with no scope. The scope filter lets a plugin token through to such an endpoint.
/// </summary>
public sealed class PluginTokenEndpointMetadata
{
    /// <summary>The one instance of the metadata.</summary>
    public static readonly PluginTokenEndpointMetadata Instance = new();

    private PluginTokenEndpointMetadata()
    {
    }
}

/// <summary>
/// Adds the rule "plugin token only" to an endpoint. See docs/decisions/0050-no-plugin-code-on-the-server.md.
/// </summary>
public static class PluginTokenEndpointExtensions
{
    /// <summary>
    /// Accepts only a plugin token on the endpoint. A web session, an API key and a delegation token get the status 403.
    /// The endpoint must also check that the plugin ID of the token is the plugin of the route. See <see cref="GetPluginId"/>.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the endpoint builder.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    /// <returns>The endpoint builder.</returns>
    public static TBuilder RequirePluginToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.RequireAuthorization(AuthPolicies.Plugin);
        builder.WithMetadata(PluginTokenEndpointMetadata.Instance);
        return builder;
    }

    /// <summary>
    /// Gets the plugin ID of a plugin token.
    /// </summary>
    /// <param name="user">The principal.</param>
    /// <returns>The plugin ID, or <see langword="null"/> when the principal does not use a plugin token.</returns>
    public static string? GetPluginId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true && user.FindFirst(ReliquaryClaims.AuthMethod)?.Value == AuthMethods.Plugin
            ? user.FindFirst(ReliquaryClaims.PluginId)?.Value
            : null;
    }
}
