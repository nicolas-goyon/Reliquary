using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Platform.Extensions;

/// <summary>The body of the activation of a plugin for the user.</summary>
/// <param name="Enabled">Whether the plugin runs for the user.</param>
/// <param name="Consent">The access that the user accepted in the consent dialog. It is necessary when the user did not accept all the access of the installed version.</param>
public sealed record PluginActivationRequest(bool? Enabled, PluginConsentRequest? Consent);

/// <summary>The access that the user accepted for a plugin version, as the consent dialog showed it.</summary>
/// <param name="Version">The plugin version that the dialog showed.</param>
/// <param name="Scopes">The scopes that the dialog showed.</param>
/// <param name="Network">The network origins that the dialog showed.</param>
/// <param name="Devices">The devices that the dialog showed.</param>
/// <param name="Sites">The linked account sites that the dialog showed.</param>
public sealed record PluginConsentRequest(string? Version, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Network, IReadOnlyList<string>? Devices, IReadOnlyList<string>? Sites);

/// <summary>The consent of the user for a plugin.</summary>
/// <param name="Version">The plugin version that the user accepted.</param>
/// <param name="Scopes">The scopes that the user accepted.</param>
/// <param name="Network">The network origins that the user accepted.</param>
/// <param name="Devices">The devices that the user accepted.</param>
/// <param name="Sites">The linked account sites that the user accepted.</param>
/// <param name="AcceptedAt">The time of the consent.</param>
public sealed record PluginConsentResponse(
    string Version,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Network,
    IReadOnlyList<string> Devices,
    IReadOnlyList<string> Sites,
    DateTimeOffset? AcceptedAt);

/// <summary>
/// The endpoint that activates a plugin for the user, with the consent of the user. See issue #287.
/// </summary>
public static class PluginActivationEndpoints
{
    /// <summary>The installed version asks for access that the user did not accept. The web app shows the consent dialog.</summary>
    public const string ConsentRequired = "consent_required";

    /// <summary>The consent does not agree with the installed version. The web app gets the extensions again and shows the dialog again.</summary>
    public const string ConsentOutdated = "consent_outdated";

    internal static void MapPluginActivationEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPut("/me/extensions/{id}", SetAsync)
            .WithTags("Extensions")
            .WithName("SetPluginActivation")
            .WithSummary("Activates or deactivates a plugin for the user. To activate a version that asks for access that the user did not accept, send the consent.")
            .RequireAuthorization(AuthPolicies.Session);
    }

    /// <summary>Gets the consent of an activation for the response, or null without a consent.</summary>
    internal static PluginConsentResponse? ToResponse(PluginActivation? activation) =>
        activation is { AcceptedVersion: { } version }
            ? new PluginConsentResponse(version, activation.Accepted.Scopes, activation.Accepted.Network, activation.Accepted.Devices, activation.Accepted.Sites, activation.AcceptedAt)
            : null;

    private static async Task<Results<NoContent, ProblemHttpResult>> SetAsync(
        string id,
        PluginActivationRequest request,
        ClaimsPrincipal user,
        ExtensionRegistry registry,
        PluginActivations activations,
        IOptions<PluginOptions> options,
        CancellationToken cancellationToken)
    {
        if (request.Enabled is not { } enabled)
        {
            return ApiProblems.Validation(new FieldError("enabled", FieldErrorCodes.Required, "The field \"enabled\" is necessary."));
        }

        if (request.Consent is { Version: null or "" })
        {
            return ApiProblems.Validation(new FieldError("consent.version", FieldErrorCodes.Required, "The consent must contain the plugin version that the dialog showed."));
        }

        var extension = await registry.FindAsync(id, cancellationToken);
        if (extension is not { Kind: "plugin", Enabled: true, Revoked: false } || !extension.Compatible)
        {
            return ApiProblems.NotFound("The instance has no enabled plugin with this ID.");
        }

        var ownerId = user.GetAccountId();
        if (!enabled)
        {
            await activations.SetEnabledAsync(ownerId, id, false, cancellationToken);
            return TypedResults.NoContent();
        }

        var asked = PluginGrant.FromManifest(extension.Manifest);
        var all = await activations.ListAsync(ownerId, cancellationToken);
        var current = all.GetValueOrDefault(id);
        if (request.Consent is { } consent)
        {
            var shown = new PluginGrant(consent.Scopes ?? [], consent.Network ?? [], consent.Devices ?? [], consent.Sites ?? []);
            if (consent.Version != extension.Version || !shown.Covers(asked))
            {
                return ApiProblems.Conflict(
                    ConsentOutdated,
                    $"The installed version of the plugin is {extension.Version}, and it asks for access that the dialog did not show. Read the access of this version, then accept again.");
            }
        }
        else if (!(current?.Accepted ?? PluginGrant.Empty).Covers(asked))
        {
            return ApiProblems.Conflict(
                ConsentRequired,
                $"The version {extension.Version} of the plugin asks for access that you did not accept. Read the access, then send your consent.");
        }

        if (await TooManyAsync(id, all, registry, options.Value.MaxActivePlugins, cancellationToken) is { } problem)
        {
            return problem;
        }

        if (request.Consent is not null)
        {
            await activations.AcceptAsync(ownerId, id, extension.Version, asked, cancellationToken);
        }
        else
        {
            await activations.SetEnabledAsync(ownerId, id, true, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Refuses one more plugin when the user has the maximum number of activated plugins.
    /// A paused plugin counts, because it runs again after the consent.
    /// </summary>
    private static async Task<ProblemHttpResult?> TooManyAsync(
        string id, IReadOnlyDictionary<string, PluginActivation> activations, ExtensionRegistry registry, int maxActive, CancellationToken cancellationToken)
    {
        var rows = await registry.ListAsync(enabledOnly: true, cancellationToken);
        var activated = rows
            .Where(row => row.Kind == "plugin" && row.Compatible && activations.GetValueOrDefault(row.Id)?.Enabled == true)
            .Select(row => row.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (activated.Contains(id) || activated.Count < maxActive)
        {
            return null;
        }

        return ApiProblems.Conflict(
            ExtensionEndpoints.TooManyActivePlugins,
            $"You have {activated.Count} active plugins, and the instance allows {maxActive}. Deactivate a plugin before you activate a different plugin.");
    }
}
