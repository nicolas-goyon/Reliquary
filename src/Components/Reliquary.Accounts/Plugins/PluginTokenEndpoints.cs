using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.Plugins;

/// <summary>A plugin token.</summary>
/// <param name="Token">The token, for the header <c>Authorization: Bearer</c>.</param>
/// <param name="ExpiresAt">The time when the token stops being valid.</param>
/// <param name="PluginId">The ID of the plugin.</param>
/// <param name="Scopes">The scopes of the token.</param>
public sealed record PluginTokenResponse(string Token, DateTimeOffset ExpiresAt, string PluginId, IReadOnlyList<string> Scopes);

/// <summary>The optional body of the request for a plugin token.</summary>
/// <param name="Scopes">
/// The scopes that the token can have. The token gets only the scopes of this list that the plugin has.
/// The web app sends the scopes of the plugin manifest for a development plugin, because the instance does not have this manifest.
/// </param>
public sealed record PluginTokenRequest(IReadOnlyList<string>? Scopes);

/// <summary>
/// The endpoint that gives a plugin token to the web app. See docs/decisions/0049-plugin-sandbox.md.
/// </summary>
internal static class PluginTokenEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/me/extensions/{id}/token", CreateAsync)
            .WithTags("Extensions")
            .WithName("CreatePluginToken")
            .WithSummary("Creates a plugin token for 10 minutes, with the scopes that the user accepted for the plugin. The web app calls the API with this token for the plugin. Only a web session can create it.")
            .RequireAuthorization(AuthPolicies.Session);
    }

    private static async Task<Results<Ok<PluginTokenResponse>, ProblemHttpResult>> CreateAsync(
        string id, PluginTokenRequest? request, ClaimsPrincipal user, IPluginAccess access, PluginTokens tokens, CancellationToken cancellationToken)
    {
        var accountId = user.GetAccountId();
        if (await access.ScopesAsync(accountId, id, cancellationToken) is not { } allowed)
        {
            return ApiProblems.NotFound("The plugin does not run for you. The instance must enable the plugin, and you must activate it.");
        }

        var scopes = request?.Scopes is { } wanted ? allowed.Where(scope => wanted.Contains(scope, StringComparer.Ordinal)).ToList() : allowed;

        var (token, expiresAt) = tokens.Create(accountId, id, scopes);
        return TypedResults.Ok(new PluginTokenResponse(token, expiresAt, id, scopes));
    }
}
