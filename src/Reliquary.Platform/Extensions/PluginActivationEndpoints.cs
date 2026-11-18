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
/// <param name="Failed">With <c>enabled: false</c>: true when the web app deactivates the plugin because it failed too many times. The API keeps the time.</param>
public sealed record PluginActivationRequest(bool? Enabled, PluginConsentRequest? Consent, bool? Failed = null);

/// <summary>The layout of a plugin for the user: the position of the plugin and the contributions that the user hides.</summary>
/// <param name="Position">The position of the contributions of the plugin among the contributions of the other plugins, from -1000 to 1000. A smaller number comes first.</param>
/// <param name="Hidden">The hidden contributions, as <c>slot:id</c>, for example <c>deck.actions:sync</c>.</param>
public sealed record PluginLayoutRequest(int? Position, IReadOnlyList<string>? Hidden);

/// <summary>The layout of a plugin for the user.</summary>
/// <param name="Position">The position of the contributions of the plugin among the contributions of the other plugins.</param>
/// <param name="Hidden">The hidden contributions, as <c>slot:id</c>.</param>
public sealed record PluginLayoutResponse(int Position, IReadOnlyList<string> Hidden);

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

    /// <summary>The maximum number of hidden contributions of one plugin.</summary>
    public const int MaxHidden = 50;

    /// <summary>The maximum length of the key of a hidden contribution.</summary>
    public const int MaxHiddenLength = 100;

    /// <summary>The maximum distance of a position from 0.</summary>
    public const int MaxPosition = 1000;

    internal static void MapPluginActivationEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPut("/me/extensions/{id}/layout", SetLayoutAsync)
            .WithTags("Extensions")
            .WithName("SetPluginLayout")
            .WithSummary("Keeps the position of a plugin and the contributions that the user hides. The user must have activated the plugin one time.")
            .RequireAuthorization(AuthPolicies.Session);

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
            await activations.SetEnabledAsync(ownerId, id, false, request.Failed == true, cancellationToken);
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
            await activations.SetEnabledAsync(ownerId, id, true, failed: false, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetLayoutAsync(
        string id, PluginLayoutRequest request, ClaimsPrincipal user, PluginActivations activations, CancellationToken cancellationToken)
    {
        var position = request.Position ?? 0;
        var hidden = (request.Hidden ?? []).Distinct(StringComparer.Ordinal).ToList();
        var errors = new List<FieldError>();
        if (position is < -MaxPosition or > MaxPosition)
        {
            errors.Add(new FieldError("position", FieldErrorCodes.OutOfRange, $"The position must be from -{MaxPosition} to {MaxPosition}."));
        }

        if (hidden.Count > MaxHidden)
        {
            errors.Add(new FieldError("hidden", FieldErrorCodes.TooLong, $"The list can contain {MaxHidden} contributions at most."));
        }
        else if (hidden.Any(key => key is null || key.Length is 0 or > MaxHiddenLength || !key.Contains(':', StringComparison.Ordinal) || key.Any(char.IsControl)))
        {
            errors.Add(new FieldError("hidden", FieldErrorCodes.InvalidFormat, $"Each hidden contribution must have the format \"slot:id\", with {MaxHiddenLength} characters at most."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        return await activations.SetLayoutAsync(user.GetAccountId(), id, position, hidden, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.NotFound("You did not activate a plugin with this ID.");
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
