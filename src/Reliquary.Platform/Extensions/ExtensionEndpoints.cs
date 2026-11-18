using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.BuildingBlocks.Versioning;

using Reliquary.Platform.Extensions.Catalog;

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
/// <param name="Panels">The panels of the plugin manifest. The web app shows each panel in its own sandboxed iframe.</param>
/// <param name="I18n">The translations of the plugin manifest, or null.</param>
/// <param name="Formats">The import formats and the export formats of the plugin manifest.</param>
/// <param name="Network">The network origins that the plugin manifest asks for.</param>
/// <param name="Devices">The devices that the plugin manifest asks for.</param>
/// <param name="Sites">The linked account sites of the plugin manifest.</param>
/// <param name="Consent">The access that the user accepted for the plugin, or null. Only for an authenticated user.</param>
/// <param name="Paused">Whether the user activated the plugin, but the installed version asks for access that the user did not accept. A paused plugin does not run.</param>
/// <param name="Layout">The position of the plugin and the contributions that the user hides, or null for an extension that the user never activated.</param>
/// <param name="DisabledByCoreAt">The time when the web app deactivated the plugin because it failed too many times, or null.</param>
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
    bool Active,
    IReadOnlyList<ExtensionPanelResponse> Panels,
    JsonElement? I18n,
    IReadOnlyList<ExtensionFormatResponse> Formats,
    IReadOnlyList<string> Network,
    IReadOnlyList<string> Devices,
    IReadOnlyList<string> Sites,
    PluginConsentResponse? Consent,
    bool Paused,
    PluginLayoutResponse? Layout = null,
    DateTimeOffset? DisabledByCoreAt = null);

/// <summary>A panel of a plugin manifest.</summary>
/// <param name="Id">The ID of the panel in the plugin.</param>
/// <param name="Slot">The slot of the panel, for example <c>analysis.tabs</c>.</param>
/// <param name="Title">The title in English.</param>
public sealed record ExtensionPanelResponse(string Id, string Slot, string Title);

/// <summary>An import format or an export format of a plugin manifest.</summary>
/// <param name="Id">The ID of the format in the plugin.</param>
/// <param name="Direction"><c>import</c> or <c>export</c>.</param>
/// <param name="Target"><c>deck</c> or <c>collection</c>.</param>
/// <param name="Title">The name in English.</param>
/// <param name="Extensions">The file name extensions, for example <c>.dek</c>.</param>
public sealed record ExtensionFormatResponse(string Id, string Direction, string Target, string Title, IReadOnlyList<string> Extensions);

/// <summary>The extensions of the instance and the version of the core API.</summary>
/// <param name="CoreApi">The current version of the core API.</param>
/// <param name="Items">The extensions.</param>
/// <param name="Plugins">The settings of the plugin runtime of the web app.</param>
public sealed record ExtensionListResponse(string CoreApi, IReadOnlyList<ExtensionResponse> Items, PluginRuntimeResponse Plugins);

/// <summary>The settings of the plugin runtime of the web app.</summary>
/// <param name="MaxActive">The maximum number of active plugins for each user.</param>
/// <param name="FrameOrigin">The origin of the plugin frames, or null for the host of the instance.</param>
/// <param name="DevelopmentPlugins">Whether a user can load a plugin from a URL. See <see cref="PluginOptions.AllowDevelopmentPlugins"/>.</param>
public sealed record PluginRuntimeResponse(int MaxActive, string? FrameOrigin, bool DevelopmentPlugins);

/// <summary>An installed extension, with its manifest and its settings, for the administrator.</summary>
/// <param name="Extension">The extension.</param>
/// <param name="Manifest">The manifest of the installation.</param>
/// <param name="Settings">The settings of the instance for the extension.</param>
/// <param name="InstalledAt">The time of the first installation.</param>
/// <param name="UpdatedAt">The time of the last change.</param>
/// <param name="RevocationReason">The reason of the revocation of the installed version by the plugin catalog, or null. A revoked version does not run.</param>
public sealed record AdminExtensionResponse(ExtensionResponse Extension, JsonElement Manifest, JsonElement Settings, DateTimeOffset InstalledAt, DateTimeOffset UpdatedAt, string? RevocationReason);

/// <summary>The body of a change of an extension. A missing field does not change.</summary>
/// <param name="Enabled">The activation flag of the instance.</param>
/// <param name="Settings">The settings of the instance for the extension, as a JSON object.</param>
public sealed record UpdateExtensionRequest(bool? Enabled, JsonElement? Settings);

/// <summary>
/// The endpoints of the extension registry.
/// </summary>
public static class ExtensionEndpoints
{
    /// <summary>The extension does not support the current core API.</summary>
    public const string CoreVersionNotSupported = "core_version_not_supported";

    /// <summary>The plugin catalog revoked this plugin version.</summary>
    public const string VersionRevoked = "version_revoked";

    /// <summary>The user has the maximum number of active plugins.</summary>
    public const string TooManyActivePlugins = "too_many_active_plugins";

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

        api.MapPluginActivationEndpoints();
        api.MapPluginStorageEndpoints();

        api.MapDevelopmentPluginEndpoints();
        api.MapPluginFileEndpoints();
        api.MapPluginCatalogEndpoints();

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

    private static async Task<Ok<ExtensionListResponse>> ListAsync(
        ClaimsPrincipal user, ExtensionRegistry registry, PluginActivations pluginActivations, IOptions<PluginOptions> options, CancellationToken cancellationToken)
    {
        var rows = await registry.ListAsync(enabledOnly: true, cancellationToken);
        var activations = user.FindAccountId() is { } ownerId && rows.Any(row => row.Kind == "plugin")
            ? await pluginActivations.ListAsync(ownerId, cancellationToken)
            : new Dictionary<string, PluginActivation>();
        return TypedResults.Ok(new ExtensionListResponse(
            CoreApi.Version.ToString(),
            rows.Select(row => ToResponse(row, activations)).ToList(),
            new PluginRuntimeResponse(
                options.Value.MaxActivePlugins,
                string.IsNullOrWhiteSpace(options.Value.FrameOrigin) ? null : options.Value.FrameOrigin.TrimEnd('/'),
                options.Value.AllowDevelopmentPlugins)));
    }

    private static async Task<Ok<IReadOnlyList<AdminExtensionResponse>>> ListAllAsync(ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        var rows = await registry.ListAsync(enabledOnly: false, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<AdminExtensionResponse>>(rows.Select(ToAdminResponse).ToList());
    }

    internal static async Task<Results<Created<AdminExtensionResponse>, Ok<AdminExtensionResponse>, ProblemHttpResult>> InstallAsync(
        string id, JsonElement body, ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        if (!TryReadManifest(body, out var manifest, out var manifestJson, out var readErrors))
        {
            return readErrors.Count > 0
                ? ApiProblems.Validation(readErrors)
                : ApiProblems.Problem(StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The manifest must be a JSON object with the fields of an extension manifest.");
        }

        var errors = readErrors.ToList();
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

        if (manifest.Kind == "plugin" && await registry.RevocationReasonAsync(id, manifest.Version!, cancellationToken) is { } reason)
        {
            return ApiProblems.Conflict(VersionRevoked, $"The plugin catalog revoked the version {manifest.Version} of {id}: {reason} Install a different version.");
        }

        var existing = await registry.FindAsync(id, cancellationToken);
        if (existing is not null && existing.Kind != manifest.Kind)
        {
            return ApiProblems.Conflict(ErrorCodes.Conflict, $"The extension {id} is installed as a {existing.Kind}. Uninstall it before you install it as a {manifest.Kind}.");
        }

        var (row, created) = await registry.InstallAsync(manifest, manifestJson, cancellationToken);
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

        if (request.Enabled == true && existing.Revoked)
        {
            return ApiProblems.Conflict(
                VersionRevoked,
                $"The plugin catalog revoked the version {existing.Version} of {id}: {existing.RevocationReason} Install a different version before you enable it.");
        }

        var settings = request.Settings is { ValueKind: JsonValueKind.Object } value ? value : (JsonElement?)null;
        var row = await registry.UpdateAsync(id, request.Enabled, settings, cancellationToken);
        return row is null ? NotInstalled() : TypedResults.Ok(ToAdminResponse(row));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UninstallAsync(string id, ExtensionRegistry registry, CancellationToken cancellationToken) =>
        await registry.UninstallAsync(id, cancellationToken) ? TypedResults.NoContent() : NotInstalled();

    /// <summary>
    /// Reads and checks the manifest of an extension.
    /// A plugin manifest must follow the rules of <see cref="PluginManifestRules"/>, and the registry keeps all its fields.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the API can read the manifest. The errors then give the incorrect fields of a module manifest.
    /// <see langword="false"/> for an incorrect plugin manifest, with its errors, or for a body that the API cannot read, with no errors.
    /// </returns>
    private static bool TryReadManifest(JsonElement body, out ExtensionManifest manifest, out string manifestJson, out IReadOnlyList<FieldError> errors)
    {
        manifest = null!;
        manifestJson = "{}";
        errors = [];
        if (body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // A manifest of the plugin catalog has no kind, so a manifest without a kind is a plugin manifest.
        var isPlugin = !body.TryGetProperty("kind", out var kind) || (kind.ValueKind == JsonValueKind.String && kind.GetString() == "plugin");
        if (isPlugin)
        {
            errors = PluginManifestCheck.Check(body);
            if (errors.Count > 0)
            {
                return false;
            }
        }

        try
        {
            manifest = body.Deserialize<ExtensionManifest>(JsonSerializerOptions.Web)!;
            if (isPlugin)
            {
                manifest = manifest with { Kind = "plugin" };
            }
        }
        catch (JsonException)
        {
            return false;
        }

        if (!isPlugin)
        {
            errors = manifest.Validate();
            manifestJson = JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web);
            return true;
        }

        manifestJson = body.GetRawText();
        return true;
    }

    private static ExtensionResponse ToResponse(ExtensionRow row, IReadOnlyDictionary<string, PluginActivation>? activations)
    {
        var isPlugin = row.Kind == "plugin";
        var asked = isPlugin ? PluginGrant.FromManifest(row.Manifest) : PluginGrant.Empty;
        var activation = isPlugin ? activations?.GetValueOrDefault(row.Id) : null;
        var active = row.Enabled && row.Compatible && !row.Revoked && (!isPlugin || (activation?.Runs(asked) ?? false));
        var (panels, i18n, formats) = ReadInterface(row.Manifest);
        return new ExtensionResponse(
            row.Id, row.Kind, row.Name, row.Version, row.CoreApi, row.Permissions, row.Slots, row.Enabled, row.Compatible, active, panels, i18n, formats,
            asked.Network, asked.Devices, asked.Sites, PluginActivationEndpoints.ToResponse(activation), activation?.IsPaused(asked) ?? false,
            activation is null ? null : new PluginLayoutResponse(activation.Settings.Position, activation.Settings.Hidden),
            activation?.Settings.DisabledByCoreAt);
    }

    /// <summary>Reads the panels, the translations and the formats of a manifest. The API checked the manifest at install time.</summary>
    private static (IReadOnlyList<ExtensionPanelResponse> Panels, JsonElement? I18n, IReadOnlyList<ExtensionFormatResponse> Formats) ReadInterface(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var panels = new List<ExtensionPanelResponse>();
        if (root.TryGetProperty("panels", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var panel in list.EnumerateArray())
            {
                if (panel.ValueKind == JsonValueKind.Object
                    && panel.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && panel.TryGetProperty("slot", out var slot) && slot.ValueKind == JsonValueKind.String
                    && panel.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                {
                    panels.Add(new ExtensionPanelResponse(id.GetString()!, slot.GetString()!, title.GetString()!));
                }
            }
        }

        JsonElement? i18n = root.TryGetProperty("i18n", out var translations) && translations.ValueKind == JsonValueKind.Object ? translations.Clone() : null;

        var formats = new List<ExtensionFormatResponse>();
        if (root.TryGetProperty("formats", out var formatList) && formatList.ValueKind == JsonValueKind.Array)
        {
            foreach (var format in formatList.EnumerateArray())
            {
                if (format.ValueKind == JsonValueKind.Object
                    && TryGetString(format, "id", out var id)
                    && TryGetString(format, "direction", out var direction)
                    && TryGetString(format, "target", out var target)
                    && TryGetString(format, "title", out var title))
                {
                    var extensions = format.TryGetProperty("extensions", out var extensionList) && extensionList.ValueKind == JsonValueKind.Array
                        ? extensionList.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
                        : [];
                    formats.Add(new ExtensionFormatResponse(id, direction, target, title, extensions));
                }
            }
        }

        return (panels, i18n, formats);
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static AdminExtensionResponse ToAdminResponse(ExtensionRow row) => new(
        ToResponse(row, null),
        ParseJson(row.Manifest),
        ParseJson(row.Settings),
        new DateTimeOffset(row.InstalledAt, TimeSpan.Zero),
        new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
        row.RevocationReason);

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ProblemHttpResult NotInstalled() => ApiProblems.NotFound("The instance has no extension with this ID.");
}
