using System.Text.Json;
using System.Text.RegularExpressions;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Versioning;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// The manifest of an extension, as the administrator installs it. See docs/architecture/extensions.md.
/// </summary>
/// <param name="Id">The ID of the extension: lowercase letters, digits and hyphens, for example <c>moxfield-sync</c>.</param>
/// <param name="Kind">The kind of extension: <c>plugin</c> or <c>module</c>.</param>
/// <param name="Name">The name of the extension.</param>
/// <param name="Version">The version of the extension, in the Semantic Versioning format.</param>
/// <param name="CoreApi">The versions of the core API that the extension supports, for example <c>^1.0</c>.</param>
/// <param name="Permissions">The permissions that the extension asks for, for example <c>decks:read</c>.</param>
/// <param name="Slots">The interface slots that the extension uses, for example <c>deck.actions</c>.</param>
/// <param name="Network">The network domains of a plugin.</param>
/// <param name="Entry">The entry point of the front-end part.</param>
/// <param name="Settings">The settings of the extension, as a JSON object.</param>
public sealed partial record ExtensionManifest(
    string? Id,
    string? Kind,
    string? Name,
    string? Version,
    string? CoreApi,
    IReadOnlyList<string>? Permissions,
    IReadOnlyList<string>? Slots,
    IReadOnlyList<string>? Network,
    string? Entry,
    JsonElement? Settings)
{
    /// <summary>The kinds of extensions.</summary>
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "plugin", "module" };

    /// <summary>
    /// Checks the fields of the manifest.
    /// </summary>
    /// <returns>The field errors. The list is empty when the manifest is valid.</returns>
    public IReadOnlyList<FieldError> Validate()
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrEmpty(Id))
        {
            errors.Add(new FieldError("id", FieldErrorCodes.Required, "The ID is necessary."));
        }
        else if (!IdPattern().IsMatch(Id))
        {
            errors.Add(new FieldError("id", FieldErrorCodes.InvalidFormat, "The ID must have 2 to 64 lowercase letters, digits and hyphens, and start with a letter or a digit."));
        }

        if (string.IsNullOrEmpty(Kind))
        {
            errors.Add(new FieldError("kind", FieldErrorCodes.Required, "The kind is necessary."));
        }
        else if (!Kinds.Contains(Kind))
        {
            errors.Add(new FieldError("kind", FieldErrorCodes.NotAllowed, "The kind must be \"plugin\" or \"module\"."));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.Required, "The name is necessary."));
        }
        else if (Name.Length > 100)
        {
            errors.Add(new FieldError("name", FieldErrorCodes.TooLong, "The name must have 100 characters or fewer."));
        }

        if (string.IsNullOrEmpty(Version))
        {
            errors.Add(new FieldError("version", FieldErrorCodes.Required, "The version is necessary."));
        }
        else if (!SemanticVersion.TryParse(Version, out _))
        {
            errors.Add(new FieldError("version", FieldErrorCodes.InvalidFormat, "The version must have the format major.minor.patch, for example 1.0.0."));
        }

        if (string.IsNullOrEmpty(CoreApi))
        {
            errors.Add(new FieldError("coreApi", FieldErrorCodes.Required, "The range of core API versions is necessary, for example \"^1.0\"."));
        }
        else if (!VersionRange.TryParse(CoreApi, out _))
        {
            errors.Add(new FieldError("coreApi", FieldErrorCodes.InvalidFormat, "The range of core API versions does not have a correct format. Use for example \"^1.0\" or \">=1.0.0 <2.0.0\"."));
        }

        AddListErrors(errors, "permissions", Permissions, PermissionPattern(), "Each permission must have the format \"area:action\", for example \"decks:read\".");
        AddListErrors(errors, "slots", Slots, SlotPattern(), "Each slot must have the format \"area.name\", for example \"deck.actions\".");

        if (Settings is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Undefined and not JsonValueKind.Null })
        {
            errors.Add(new FieldError("settings", FieldErrorCodes.InvalidFormat, "The settings must be a JSON object."));
        }

        return errors;
    }

    /// <summary>
    /// Tells whether the current core API is in the range of the manifest.
    /// </summary>
    /// <returns><see langword="true"/> when the extension supports the current core API.</returns>
    public bool SupportsCurrentCore() =>
        VersionRange.TryParse(CoreApi, out var range) && range!.Contains(BuildingBlocks.Versioning.CoreApi.Version);

    private static void AddListErrors(List<FieldError> errors, string field, IReadOnlyList<string>? values, Regex pattern, string message)
    {
        if (values is null)
        {
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null || !pattern.IsMatch(values[index]))
            {
                errors.Add(new FieldError($"{field}[{index}]", FieldErrorCodes.InvalidFormat, message));
            }
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]*:[a-z][a-z0-9-]*$")]
    private static partial Regex PermissionPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$")]
    private static partial Regex SlotPattern();
}
