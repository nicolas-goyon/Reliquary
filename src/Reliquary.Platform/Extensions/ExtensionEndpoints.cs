using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.BuildingBlocks.Versioning;

namespace Reliquary.Platform.Extensions;

/// <summary>An installed extension.</summary>
/// <param name="Id">The ID of the extension.</param>
/// <param name="Kind">The kind: <c>plugin</c> or <c>module</c>.</param>
/// <param name="Name">The name.</param>
/// <param name="Version">The version.</param>
/// <param name="CoreApi">The versions of the core API that the extension supports.</param>
/// <param name="Permissions">The permissions that the extension asks for.</param>
/// <param name="Slots">The interface slots that the extension uses.</param>
/// <param name="Enabled">The activation flag of the instance.</param>
/// <param name="Compatible">Whether the extension supports the current core API. An incompatible extension does not run.</param>
/// <param name="Active">Whether the extension runs for the user: a module when the instance enables it, a plugin when the user also activates it.</param>
public sealed record ExtensionResponse(
    string Id,
    string Kind,
    string Name,
    string Version,
    string CoreApi,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Slots,
    bool Enabled,
    bool Compatible,
    bool Active);

/// <summary>The extensions of the instance and the version of the core API.</summary>
/// <param name="CoreApi">The current version of the core API.</param>
/// <param name="Items">The extensions.</param>
public sealed record ExtensionListResponse(string CoreApi, IReadOnlyList<ExtensionResponse> Items);

/// <summary>An installed extension, with its manifest and its settings, for the administrator.</summary>
/// <param name="Extension">The extension.</param>
/// <param name="Manifest">The manifest of the installation.</param>
/// <param name="Settings">The settings of the instance for the extension.</param>
/// <param name="InstalledAt">The time of the first installation.</param>
/// <param name="UpdatedAt">The time of the last change.</param>
public sealed record AdminExtensionResponse(ExtensionResponse Extension, JsonElement Manifest, JsonElement Settings, DateTimeOffset InstalledAt, DateTimeOffset UpdatedAt);

/// <summary>The body of a change of an extension. A missing field does not change.</summary>
/// <param name="Enabled">The activation flag of the instance.</param>
/// <param name="Settings">The settings of the instance for the extension, as a JSON object.</param>
public sealed record UpdateExtensionRequest(bool? Enabled, JsonElement? Settings);

/// <summary>The body of the activation of a plugin for the user.</summary>
/// <param name="Enabled">Whether the plugin runs for the user.</param>
public sealed record PluginActivationRequest(bool? Enabled);

/// <summary>
/// The endpoints of the extension registry.
/// </summary>
public static class ExtensionEndpoints
{
    /// <summary>The extension does not support the current core API.</summary>
    public const string CoreVersionNotSupported = "core_version_not_supported";

    /// <summary>
    /// Adds the endpoints of the extension registry to the public API.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    public static void MapExtensionEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/extensions", ListAsync)
            .WithTags("Extensions")
            .WithName("ListExtensions")
            .WithSummary("Lists the extensions that the instance enables. For an authenticated user, Active tells if the extension runs for the user.");

        api.MapPut("/me/extensions/{id}", SetPluginActivationAsync)
            .WithTags("Extensions")
            .WithName("SetPluginActivation")
            .WithSummary("Activates or deactivates a plugin for the user.")
            .RequireAuthorization(AuthPolicies.Session);

        var admin = api.MapGroup("/admin/extensions").WithTags("Administration").RequireAuthorization(AuthPolicies.Administrator);

        admin.MapGet(string.Empty, ListAllAsync)
            .WithName("ListInstalledExtensions")
            .WithSummary("Lists all the installed extensions, also the disabled and incompatible extensions.");

        admin.MapPut("/{id}", InstallAsync)
            .WithName("InstallExtension")
            .WithSummary("Installs an extension from its manifest, or updates the manifest. A new extension is disabled.");

        admin.MapPatch("/{id}", UpdateAsync)
            .WithName("UpdateExtension")
            .WithSummary("Enables or disables an extension on the instance, or changes its settings.");

        admin.MapDelete("/{id}", UninstallAsync)
            .WithName("UninstallExtension")
            .WithSummary("Uninstalls an extension.");
    }

    private static async Task<Ok<ExtensionListResponse>> ListAsync(ClaimsPrincipal user, ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        var rows = await registry.ListAsync(enabledOnly: true, cancellationToken);
        var activations = user.FindAccountId() is { } ownerId && rows.Any(row => row.Kind == "plugin")
            ? await registry.PluginActivationsAsync(ownerId, cancellationToken)
            : new Dictionary<string, bool>();
        return TypedResults.Ok(new ExtensionListResponse(
            CoreApi.Version.ToString(),
            rows.Select(row => ToResponse(row, activations)).ToList()));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetPluginActivationAsync(
        string id, PluginActivationRequest request, ClaimsPrincipal user, ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        if (request.Enabled is not { } enabled)
        {
            return ApiProblems.Validation(new FieldError("enabled", FieldErrorCodes.Required, "The field \"enabled\" is necessary."));
        }

        var extension = await registry.FindAsync(id, cancellationToken);
        if (extension is not { Kind: "plugin", Enabled: true } || !extension.Compatible)
        {
            return ApiProblems.NotFound("The instance has no enabled plugin with this ID.");
        }

        await registry.SetPluginActivationAsync(user.GetAccountId(), id, enabled, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<AdminExtensionResponse>>> ListAllAsync(ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        var rows = await registry.ListAsync(enabledOnly: false, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<AdminExtensionResponse>>(rows.Select(ToAdminResponse).ToList());
    }

    private static async Task<Results<Created<AdminExtensionResponse>, Ok<AdminExtensionResponse>, ProblemHttpResult>> InstallAsync(
        string id, ExtensionManifest manifest, ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        var errors = manifest.Validate().ToList();
        if (manifest.Id is not null && manifest.Id != id)
        {
            errors.Add(new FieldError("id", FieldErrorCodes.NotAllowed, "The ID of the manifest must be the same as the ID in the path."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        if (!manifest.SupportsCurrentCore())
        {
            return ApiProblems.Conflict(
                CoreVersionNotSupported,
                $"The extension supports the core API versions \"{manifest.CoreApi}\", but the core API version is {CoreApi.Version}. Install a version of the extension for this core.");
        }

        var existing = await registry.FindAsync(id, cancellationToken);
        if (existing is not null && existing.Kind != manifest.Kind)
        {
            return ApiProblems.Conflict(ErrorCodes.Conflict, $"The extension {id} is installed as a {existing.Kind}. Uninstall it before you install it as a {manifest.Kind}.");
        }

        var (row, created) = await registry.InstallAsync(manifest, cancellationToken);
        var response = ToAdminResponse(row);
        return created
            ? TypedResults.Created($"{ApiRoutes.Base}/admin/extensions/{row.Id}", response)
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<AdminExtensionResponse>, ProblemHttpResult>> UpdateAsync(
        string id, UpdateExtensionRequest request, ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        if (request.Settings is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Null and not JsonValueKind.Undefined })
        {
            return ApiProblems.Validation(new FieldError("settings", FieldErrorCodes.InvalidFormat, "The settings must be a JSON object."));
        }

        var existing = await registry.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotInstalled();
        }

        if (request.Enabled == true && !existing.Compatible)
        {
            return ApiProblems.Conflict(
                CoreVersionNotSupported,
                $"The extension supports the core API versions \"{existing.CoreApi}\", but the core API version is {CoreApi.Version}. Install a compatible version before you enable it.");
        }

        var settings = request.Settings is { ValueKind: JsonValueKind.Object } value ? value : (JsonElement?)null;
        var row = await registry.UpdateAsync(id, request.Enabled, settings, cancellationToken);
        return row is null ? NotInstalled() : TypedResults.Ok(ToAdminResponse(row));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UninstallAsync(string id, ExtensionRegistry registry, CancellationToken cancellationToken) =>
        await registry.UninstallAsync(id, cancellationToken) ? TypedResults.NoContent() : NotInstalled();

    private static ExtensionResponse ToResponse(ExtensionRow row, IReadOnlyDictionary<string, bool>? activations)
    {
        var active = row.Enabled && row.Compatible && (row.Kind == "module" || (activations?.GetValueOrDefault(row.Id) ?? false));
        return new ExtensionResponse(row.Id, row.Kind, row.Name, row.Version, row.CoreApi, row.Permissions, row.Slots, row.Enabled, row.Compatible, active);
    }

    private static AdminExtensionResponse ToAdminResponse(ExtensionRow row) => new(
        ToResponse(row, null),
        ParseJson(row.Manifest),
        ParseJson(row.Settings),
        new DateTimeOffset(row.InstalledAt, TimeSpan.Zero),
        new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ProblemHttpResult NotInstalled() => ApiProblems.NotFound("The instance has no extension with this ID.");
}
