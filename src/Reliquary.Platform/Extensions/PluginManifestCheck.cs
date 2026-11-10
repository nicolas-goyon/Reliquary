using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Versioning;
using static Reliquary.Platform.Extensions.PluginManifestRules;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// Checks a plugin manifest against the rules of <see cref="PluginManifestRules"/>.
/// The check also reads the version and the range of core API versions, which the JSON Schema cannot check.
/// </summary>
public static partial class PluginManifestCheck
{
    /// <summary>
    /// Checks a plugin manifest.
    /// </summary>
    /// <param name="manifest">The manifest, as a JSON value.</param>
    /// <returns>The field errors. The list is empty when the manifest is valid.</returns>
    public static IReadOnlyList<FieldError> Check(JsonElement manifest)
    {
        var errors = new Errors();
        if (manifest.ValueKind != JsonValueKind.Object)
        {
            errors.Add(string.Empty, FieldErrorCodes.InvalidFormat, "The manifest must be a JSON object.");
            return errors.List;
        }

        errors.UnknownFields(manifest, string.Empty, Fields);
        errors.RequiredFields(manifest, string.Empty, RequiredFields);

        Text(errors, manifest, "$schema", null, null);
        Text(errors, manifest, "id", IdRegex(), "The ID must have 2 to 64 lowercase letters, digits and hyphens, and start with a letter or a digit.");
        if (manifest.TryGetProperty("kind", out var kind) && !(kind.ValueKind == JsonValueKind.String && kind.GetString() == "plugin"))
        {
            errors.Add("kind", FieldErrorCodes.NotAllowed, "The kind of a plugin manifest must be \"plugin\".");
        }

        NonEmptyText(errors, manifest, "name", NameMaxLength);
        Text(errors, manifest, "description", null, null, DescriptionMaxLength);
        if (Text(errors, manifest, "version", VersionRegex(), "The version must have the format major.minor.patch, for example 1.0.0 or 1.0.0-beta.1. Build metadata is not permitted.", VersionMaxLength) is { } version
            && !SemanticVersion.TryParse(version, out _))
        {
            errors.Add("version", FieldErrorCodes.InvalidFormat, "The version must have the format major.minor.patch, for example 1.0.0. Each number must have 9 digits or fewer.");
        }

        if (NonEmptyText(errors, manifest, "coreApi", CoreApiMaxLength) is { } coreApi && !VersionRange.TryParse(coreApi, out _))
        {
            errors.Add("coreApi", FieldErrorCodes.InvalidFormat, "The range of core API versions does not have a correct format. Use for example \"^1.0\" or \">=1.0.0 <2.0.0\".");
        }

        Text(errors, manifest, "entry", EntryRegex(), "The entry must be a relative path to a .js or .mjs file, for example dist/index.js. A path segment cannot start with a dot.", EntryMaxLength);
        ValueList(errors, manifest, "permissions", null, value => Allowed(value, Permissions, "Each permission must be one of these scopes"));
        var slots = ValueList(errors, manifest, "slots", MaxSlots, value => Matches(value, SlotRegex(), SlotMaxLength, "Each slot must have the format \"area.name\", for example \"deck.actions\"."));
        ValueList(errors, manifest, "network", MaxNetwork, value => Matches(value, NetworkRegex(), NetworkMaxLength, "Each origin must be an HTTPS origin, for example \"https://api.example.com\", or \"http://localhost\" or \"http://127.0.0.1\". An origin can have a port. A wildcard is permitted only for one level, for example \"https://*.example.com\"."));
        ValueList(errors, manifest, "devices", null, value => Allowed(value, Devices, "Each device must be one of these values"));
        Accounts(errors, manifest);
        Panels(errors, manifest, slots);
        Formats(errors, manifest);
        Texts(errors, manifest);
        if (manifest.TryGetProperty("settings", out var settings) && settings.ValueKind != JsonValueKind.Object)
        {
            errors.Add("settings", FieldErrorCodes.InvalidFormat, "The settings must be a JSON object.");
        }

        return errors.List;
    }

    private static void Accounts(Errors errors, JsonElement manifest)
    {
        var sites = new HashSet<string>(StringComparer.Ordinal);
        ObjectList(errors, manifest, "accounts", MaxAccounts, ["site", "auth", "sync"], ["site", "auth"], (account, path) =>
        {
            if (Text(errors, account, "site", SiteRegex(), "The site must be a host name, for example \"moxfield.com\".", SiteMaxLength, path) is { } site && !sites.Add(site))
            {
                errors.Add($"{path}.site", FieldErrorCodes.NotAllowed, "Each site can occur only one time in the accounts.");
            }

            Choice(errors, account, "auth", AuthMethods, path);
            if (!account.TryGetProperty("sync", out var sync))
            {
                return;
            }

            var syncPath = $"{path}.sync";
            if (sync.ValueKind != JsonValueKind.Object)
            {
                errors.Add(syncPath, FieldErrorCodes.InvalidFormat, "The sync must be a JSON object.");
                return;
            }

            errors.UnknownFields(sync, syncPath, ["mode", "intervalMinutes"]);
            errors.RequiredFields(sync, syncPath, ["mode"]);
            Choice(errors, sync, "mode", SyncModes, syncPath);
            if (sync.TryGetProperty("intervalMinutes", out var interval))
            {
                if (interval.ValueKind != JsonValueKind.Number || !interval.TryGetInt32(out var minutes))
                {
                    errors.Add($"{syncPath}.intervalMinutes", FieldErrorCodes.InvalidFormat, "The interval must be an integer number of minutes.");
                }
                else if (minutes is < MinSyncIntervalMinutes or > MaxSyncIntervalMinutes)
                {
                    errors.Add($"{syncPath}.intervalMinutes", FieldErrorCodes.OutOfRange, $"The interval must be from {MinSyncIntervalMinutes} to {MaxSyncIntervalMinutes} minutes.");
                }
            }
        });
    }

    private static void Panels(Errors errors, JsonElement manifest, IReadOnlyList<string> slots)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        ObjectList(errors, manifest, "panels", MaxPanels, ["id", "slot", "title"], ["id", "slot", "title"], (panel, path) =>
        {
            ItemId(errors, panel, path, ids, "panels");
            if (Choice(errors, panel, "slot", PanelSlots, path) is { } slot && !slots.Contains(slot, StringComparer.Ordinal))
            {
                errors.Add($"{path}.slot", FieldErrorCodes.NotAllowed, $"The slot \"{slot}\" of the panel must also be in the field \"slots\".");
            }

            NonEmptyText(errors, panel, "title", TitleMaxLength, path);
        });
    }

    private static void Formats(Errors errors, JsonElement manifest)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        ObjectList(errors, manifest, "formats", MaxFormats, ["id", "direction", "target", "title", "extensions"], ["id", "direction", "target", "title"], (format, path) =>
        {
            ItemId(errors, format, path, ids, "formats");
            Choice(errors, format, "direction", FormatDirections, path);
            Choice(errors, format, "target", FormatTargets, path);
            NonEmptyText(errors, format, "title", TitleMaxLength, path);
            ValueList(errors, format, "extensions", MaxFileExtensions, value => Matches(value, FileExtensionRegex(), null, "Each extension must be a dot and 1 to 10 lowercase letters and digits, for example \".dek\"."), path);
        });
    }

    private static void Texts(Errors errors, JsonElement manifest)
    {
        if (!manifest.TryGetProperty("i18n", out var i18n))
        {
            return;
        }

        if (i18n.ValueKind != JsonValueKind.Object)
        {
            errors.Add("i18n", FieldErrorCodes.InvalidFormat, "The field \"i18n\" must be a JSON object, with one object of texts for each language.");
            return;
        }

        var locales = i18n.EnumerateObject().ToList();
        if (locales.Count > MaxLocales)
        {
            errors.Add("i18n", FieldErrorCodes.TooLong, $"The field \"i18n\" must have {MaxLocales} languages or fewer.");
        }

        foreach (var locale in locales)
        {
            var path = $"i18n.{locale.Name}";
            if (!LocaleRegex().IsMatch(locale.Name))
            {
                errors.Add(path, FieldErrorCodes.InvalidFormat, "Each language code must have two lowercase letters and an optional region, for example \"fr\" or \"fr-CA\".");
            }

            if (locale.Value.ValueKind != JsonValueKind.Object)
            {
                errors.Add(path, FieldErrorCodes.InvalidFormat, "The texts of a language must be a JSON object.");
                continue;
            }

            var texts = locale.Value.EnumerateObject().ToList();
            if (texts.Count > MaxTexts)
            {
                errors.Add(path, FieldErrorCodes.TooLong, $"Each language must have {MaxTexts} texts or fewer.");
            }

            foreach (var text in texts)
            {
                var textPath = $"{path}.{text.Name}";
                if (text.Name.Length > TextKeyMaxLength)
                {
                    errors.Add(textPath, FieldErrorCodes.TooLong, $"Each text key must have {TextKeyMaxLength} characters or fewer.");
                }
                else if (!TextKeyRegex().IsMatch(text.Name))
                {
                    errors.Add(textPath, FieldErrorCodes.InvalidFormat, "Each text key must have parts with a dot between them, for example \"panels.stats.title\". The key must start with a lowercase letter.");
                }

                if (text.Value.ValueKind != JsonValueKind.String)
                {
                    errors.Add(textPath, FieldErrorCodes.InvalidFormat, "Each text must be a JSON string.");
                }
                else if (text.Value.GetString()!.Length > TextMaxLength)
                {
                    errors.Add(textPath, FieldErrorCodes.TooLong, $"Each text must have {TextMaxLength} characters or fewer.");
                }
            }
        }
    }

    private static void ItemId(Errors errors, JsonElement item, string path, HashSet<string> ids, string list)
    {
        if (Text(errors, item, "id", ItemIdRegex(), "The ID must have 1 to 32 lowercase letters, digits and hyphens, and start with a letter.", null, path) is { } id && !ids.Add(id))
        {
            errors.Add($"{path}.id", FieldErrorCodes.NotAllowed, $"Each ID can occur only one time in the field \"{list}\".");
        }
    }

    /// <summary>Checks an optional text field. Returns the text when it is valid.</summary>
    private static string? Text(Errors errors, JsonElement parent, string name, Regex? pattern, string? patternMessage, int? maxLength = null, string? parentPath = null)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        var path = Join(parentPath, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(path, FieldErrorCodes.InvalidFormat, $"The field \"{name}\" must be a JSON string.");
            return null;
        }

        var text = value.GetString()!;
        if (maxLength is { } max && text.Length > max)
        {
            errors.Add(path, FieldErrorCodes.TooLong, $"The field \"{name}\" must have {max} characters or fewer.");
            return null;
        }

        if (pattern is not null && !pattern.IsMatch(text))
        {
            errors.Add(path, FieldErrorCodes.InvalidFormat, patternMessage!);
            return null;
        }

        return text;
    }

    /// <summary>Checks a text field that cannot be empty. Returns the text when it is valid.</summary>
    private static string? NonEmptyText(Errors errors, JsonElement parent, string name, int maxLength, string? parentPath = null)
    {
        var text = Text(errors, parent, name, null, null, maxLength, parentPath);
        if (text is not null && string.IsNullOrWhiteSpace(text))
        {
            errors.Add(Join(parentPath, name), FieldErrorCodes.Required, $"The field \"{name}\" cannot be empty.");
            return null;
        }

        return text;
    }

    /// <summary>Checks a text field with a fixed set of values. Returns the value when it is valid.</summary>
    private static string? Choice(Errors errors, JsonElement parent, string name, IReadOnlyList<string> values, string parentPath)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        var message = Allowed(value, values, $"The field \"{name}\" must be one of these values");
        if (message is not null)
        {
            errors.Add($"{parentPath}.{name}", message.Value.Code, message.Value.Message);
            return null;
        }

        return value.GetString();
    }

    /// <summary>Checks a list of texts with no duplicates. Returns the valid texts.</summary>
    private static List<string> ValueList(Errors errors, JsonElement parent, string name, int? maxItems, Func<JsonElement, (string Code, string Message)?> check, string? parentPath = null)
    {
        var valid = new List<string>();
        if (List(errors, parent, name, maxItems, parentPath) is not { } items)
        {
            return valid;
        }

        var path = Join(parentPath, name);
        for (var index = 0; index < items.Count; index++)
        {
            if (check(items[index]) is { } error)
            {
                errors.Add($"{path}[{index}]", error.Code, error.Message);
            }
            else if (valid.Contains(items[index].GetString()!, StringComparer.Ordinal))
            {
                errors.Add($"{path}[{index}]", FieldErrorCodes.NotAllowed, $"Each value can occur only one time in the field \"{name}\".");
            }
            else
            {
                valid.Add(items[index].GetString()!);
            }
        }

        return valid;
    }

    /// <summary>Checks a list of JSON objects, then calls <paramref name="check"/> for each object.</summary>
    private static void ObjectList(
        Errors errors, JsonElement parent, string name, int maxItems, IReadOnlyList<string> fields, IReadOnlyList<string> requiredFields, Action<JsonElement, string> check)
    {
        if (List(errors, parent, name, maxItems, null) is not { } items)
        {
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var path = $"{name}[{index}]";
            if (items[index].ValueKind != JsonValueKind.Object)
            {
                errors.Add(path, FieldErrorCodes.InvalidFormat, $"Each item of the field \"{name}\" must be a JSON object.");
                continue;
            }

            errors.UnknownFields(items[index], path, fields);
            errors.RequiredFields(items[index], path, requiredFields);
            check(items[index], path);
        }
    }

    private static List<JsonElement>? List(Errors errors, JsonElement parent, string name, int? maxItems, string? parentPath)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        var path = Join(parentPath, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add(path, FieldErrorCodes.InvalidFormat, $"The field \"{name}\" must be a JSON array.");
            return null;
        }

        var items = value.EnumerateArray().ToList();
        if (maxItems is { } max && items.Count > max)
        {
            errors.Add(path, FieldErrorCodes.TooLong, $"The field \"{name}\" must have {max} items or fewer.");
            return null;
        }

        return items;
    }

    private static (string Code, string Message)? Allowed(JsonElement value, IReadOnlyList<string> values, string message) =>
        value.ValueKind == JsonValueKind.String && values.Contains(value.GetString()!, StringComparer.Ordinal)
            ? null
            : (FieldErrorCodes.NotAllowed, $"{message}: {string.Join(", ", values.Select(item => $"\"{item}\""))}.");

    private static (string Code, string Message)? Matches(JsonElement value, Regex pattern, int? maxLength, string message)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return (FieldErrorCodes.InvalidFormat, "Each item must be a JSON string.");
        }

        var text = value.GetString()!;
        if (maxLength is { } max && text.Length > max)
        {
            return (FieldErrorCodes.TooLong, $"Each item must have {max.ToString(CultureInfo.InvariantCulture)} characters or fewer.");
        }

        return pattern.IsMatch(text) ? null : (FieldErrorCodes.InvalidFormat, message);
    }

    private static string Join(string? parentPath, string name) => string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}.{name}";

    [GeneratedRegex(IdPattern)]
    private static partial Regex IdRegex();

    [GeneratedRegex(VersionPattern)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(EntryPattern)]
    private static partial Regex EntryRegex();

    [GeneratedRegex(SlotPattern)]
    private static partial Regex SlotRegex();

    [GeneratedRegex(NetworkPattern)]
    private static partial Regex NetworkRegex();

    [GeneratedRegex(SitePattern)]
    private static partial Regex SiteRegex();

    [GeneratedRegex(ItemIdPattern)]
    private static partial Regex ItemIdRegex();

    [GeneratedRegex(FileExtensionPattern)]
    private static partial Regex FileExtensionRegex();

    [GeneratedRegex(LocalePattern)]
    private static partial Regex LocaleRegex();

    [GeneratedRegex(TextKeyPattern)]
    private static partial Regex TextKeyRegex();

    /// <summary>The errors of one check.</summary>
    private sealed class Errors
    {
        public List<FieldError> List { get; } = [];

        public void Add(string field, string code, string message) => List.Add(new FieldError(field, code, message));

        public void UnknownFields(JsonElement value, string path, IReadOnlyList<string> fields)
        {
            foreach (var property in value.EnumerateObject().Where(property => !fields.Contains(property.Name, StringComparer.Ordinal)))
            {
                Add(Join(path, property.Name), FieldErrorCodes.NotAllowed, $"The field \"{property.Name}\" is not a field of the plugin manifest. Remove it.");
            }
        }

        public void RequiredFields(JsonElement value, string path, IReadOnlyList<string> fields)
        {
            foreach (var field in fields.Where(field => !value.TryGetProperty(field, out _)))
            {
                Add(Join(path, field), FieldErrorCodes.Required, $"The field \"{field}\" is necessary.");
            }
        }
    }
}
