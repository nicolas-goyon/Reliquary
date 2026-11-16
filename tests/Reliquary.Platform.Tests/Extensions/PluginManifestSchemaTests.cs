using System.Globalization;
using System.Text.Json;
using Reliquary.Platform.Extensions;
using static Reliquary.Platform.Extensions.PluginManifestRules;

namespace Reliquary.Platform.Tests.Extensions;

/// <summary>
/// Compares the JSON Schema of the SDK with the rules of the API.
/// A change to one of the two files without a change to the other makes this test fail.
/// </summary>
public sealed class PluginManifestSchemaTests
{
    private static readonly string[] Annotations = ["$schema", "$id", "title", "description"];

    private static readonly string[] PluginKind = ["plugin"];

    [Fact]
    public void SchemaHasTheRulesOfTheApi()
    {
        var schema = ReadSchema();
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);

        Flatten(schema, string.Empty, actual);

        Assert.Equal(Expected(), actual);
    }

    [Fact]
    public void SchemaIsTheDraft202012()
    {
        var schema = ReadSchema();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.GetProperty("$schema").GetString());
    }

    [Fact]
    public void ExampleOfTheDocumentationIsValid()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "architecture", "extensions.md"))
            .Split("```json")[1].Split("```")[0]);

        Assert.Empty(PluginManifestCheck.Check(document.RootElement));
    }

    private static SortedDictionary<string, string> Expected()
    {
        var rules = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Add(string path, string keyword, object value) => rules[$"{path}#{keyword}"] = Canonical(value);
        void Text(string path, int? minLength, int? maxLength, string? pattern)
        {
            Add(path, "type", "string");
            if (minLength is { } min)
            {
                Add(path, "minLength", min);
            }

            if (maxLength is { } max)
            {
                Add(path, "maxLength", max);
            }

            if (pattern is not null)
            {
                Add(path, "pattern", pattern);
            }
        }

        void Array(string path, int? maxItems, bool unique)
        {
            Add(path, "type", "array");
            if (maxItems is { } max)
            {
                Add(path, "maxItems", max);
            }

            if (unique)
            {
                Add(path, "uniqueItems", true);
            }
        }

        void Object(string path, IReadOnlyList<string> fields, IReadOnlyList<string> required)
        {
            Add(path, "type", "object");
            Add(path, "properties", Sorted(fields));
            Add(path, "required", Sorted(required));
            Add(path, "additionalProperties", false);
        }

        Object(string.Empty, Fields, RequiredFields);
        Text("/$schema", null, null, null);
        Text("/id", null, null, IdPattern);
        Add("/kind", "enum", PluginKind);
        Text("/name", 1, NameMaxLength, null);
        Text("/description", null, DescriptionMaxLength, null);
        Text("/version", null, VersionMaxLength, VersionPattern);
        Text("/coreApi", 1, CoreApiMaxLength, null);
        Text("/entry", null, EntryMaxLength, EntryPattern);

        Array("/permissions", null, unique: true);
        Add("/permissions/items", "enum", Permissions);
        Array("/slots", MaxSlots, unique: true);
        Text("/slots/items", null, SlotMaxLength, SlotPattern);
        Array("/network", MaxNetwork, unique: true);
        Text("/network/items", null, NetworkMaxLength, NetworkPattern);
        Array("/devices", null, unique: true);
        Add("/devices/items", "enum", Devices);

        Array("/accounts", MaxAccounts, unique: false);
        Object("/accounts/items", ["site", "auth", "sync"], ["site", "auth"]);
        Text("/accounts/items/site", null, SiteMaxLength, SitePattern);
        Add("/accounts/items/auth", "enum", AuthMethods);
        Object("/accounts/items/sync", ["mode", "intervalMinutes"], ["mode"]);
        Add("/accounts/items/sync/mode", "enum", SyncModes);
        Add("/accounts/items/sync/intervalMinutes", "type", "integer");
        Add("/accounts/items/sync/intervalMinutes", "minimum", MinSyncIntervalMinutes);
        Add("/accounts/items/sync/intervalMinutes", "maximum", MaxSyncIntervalMinutes);

        Array("/panels", MaxPanels, unique: false);
        Object("/panels/items", ["id", "slot", "title"], ["id", "slot", "title"]);
        Text("/panels/items/id", null, null, ItemIdPattern);
        Add("/panels/items/slot", "enum", PanelSlots);
        Text("/panels/items/title", 1, TitleMaxLength, null);

        Array("/formats", MaxFormats, unique: false);
        Object("/formats/items", ["id", "direction", "target", "title", "extensions"], ["id", "direction", "target", "title"]);
        Text("/formats/items/id", null, null, ItemIdPattern);
        Add("/formats/items/direction", "enum", FormatDirections);
        Add("/formats/items/target", "enum", FormatTargets);
        Text("/formats/items/title", 1, TitleMaxLength, null);
        Array("/formats/items/extensions", MaxFileExtensions, unique: true);
        Text("/formats/items/extensions/items", null, null, FileExtensionPattern);

        Add("/i18n", "type", "object");
        Add("/i18n", "maxProperties", MaxLocales);
        Add("/i18n/propertyNames", "pattern", LocalePattern);
        Add("/i18n/additionalProperties", "type", "object");
        Add("/i18n/additionalProperties", "maxProperties", MaxTexts);
        Add("/i18n/additionalProperties/propertyNames", "maxLength", TextKeyMaxLength);
        Add("/i18n/additionalProperties/propertyNames", "pattern", TextKeyPattern);
        Text("/i18n/additionalProperties/additionalProperties", null, TextMaxLength, null);

        Add("/settings", "type", "object");
        return rules;
    }

    /// <summary>
    /// Writes each rule of the schema as "path#keyword" and a canonical value.
    /// An unknown keyword makes the test fail, so a new keyword needs a new rule in the API.
    /// </summary>
    private static void Flatten(JsonElement schema, string path, IDictionary<string, string> rules)
    {
        foreach (var keyword in schema.EnumerateObject())
        {
            var key = $"{path}#{keyword.Name}";
            switch (keyword.Name)
            {
                case var name when Annotations.Contains(name):
                    break;
                case "properties":
                    rules[key] = Canonical(Sorted(keyword.Value.EnumerateObject().Select(property => property.Name)));
                    foreach (var property in keyword.Value.EnumerateObject())
                    {
                        Flatten(property.Value, $"{path}/{property.Name}", rules);
                    }

                    break;
                case "required":
                    rules[key] = Canonical(Sorted(keyword.Value.EnumerateArray().Select(item => item.GetString()!)));
                    break;
                case "items" or "propertyNames":
                    Flatten(keyword.Value, keyword.Name == "items" ? $"{path}/items" : $"{path}/propertyNames", rules);
                    break;
                case "additionalProperties" when keyword.Value.ValueKind == JsonValueKind.Object:
                    Flatten(keyword.Value, $"{path}/additionalProperties", rules);
                    break;
                case "additionalProperties" or "uniqueItems":
                    rules[key] = Canonical(keyword.Value.GetBoolean());
                    break;
                case "enum":
                    rules[key] = Canonical(keyword.Value.EnumerateArray().Select(item => item.GetString()!).ToList());
                    break;
                case "type" or "pattern":
                    rules[key] = Canonical(keyword.Value.GetString()!);
                    break;
                case "minLength" or "maxLength" or "maxItems" or "minimum" or "maximum" or "maxProperties":
                    rules[key] = Canonical(keyword.Value.GetInt32());
                    break;
                default:
                    Assert.Fail($"The test does not know the keyword \"{keyword.Name}\" at \"{path}\". Add the rule to PluginManifestCheck and to this test.");
                    break;
            }
        }
    }

    private static List<string> Sorted(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToList();

    private static string Canonical(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        int number => number.ToString(CultureInfo.InvariantCulture),
        IEnumerable<string> list => "[" + string.Join(", ", list) + "]",
        _ => throw new ArgumentException($"The test cannot compare a value of type {value.GetType()}.", nameof(value)),
    };

    private static JsonElement ReadSchema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "sdk", "schema", "manifest.schema.json")));
        return document.RootElement.Clone();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reliquary.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The test cannot find Reliquary.slnx in a parent folder.");
    }
}
