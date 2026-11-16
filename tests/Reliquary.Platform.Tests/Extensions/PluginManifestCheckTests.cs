using System.Text.Json;
using System.Text.Json.Nodes;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Platform.Extensions;

namespace Reliquary.Platform.Tests.Extensions;

public sealed class PluginManifestCheckTests
{
    private const string Valid = """
        {
          "$schema": "https://example.org/manifest.schema.json",
          "id": "card-scan",
          "kind": "plugin",
          "name": "Card scan",
          "description": "Scans cards with the camera.",
          "version": "1.2.0-beta.1",
          "coreApi": "^1.0",
          "entry": "dist/index.mjs",
          "permissions": ["decks:read", "collection:write"],
          "slots": ["analysis.tabs", "plugin.page", "deck.import-formats"],
          "network": ["https://api.example.com", "https://*.example.org:8443", "http://localhost:5000", "http://127.0.0.1"],
          "devices": ["camera", "loopback-network"],
          "accounts": [
            { "site": "moxfield.com", "auth": "public-username", "sync": { "mode": "periodic", "intervalMinutes": 15 } },
            { "site": "archidekt.com", "auth": "api-key" }
          ],
          "panels": [
            { "id": "stats", "slot": "analysis.tabs", "title": "Scan statistics" },
            { "id": "scan", "slot": "plugin.page", "title": "Scan" }
          ],
          "formats": [
            { "id": "dek", "direction": "import", "target": "deck", "title": "MTGO deck", "extensions": [".dek"] }
          ],
          "i18n": {
            "fr": { "name": "Scan de cartes", "panels.stats.title": "Statistiques du scan" },
            "fr-CA": { "formats.dek.title": "Deck MTGO" }
          },
          "settings": { "relay": false }
        }
        """;

    private const string Minimal = """{ "id": "hello", "name": "Hello", "version": "0.1.0", "coreApi": "^1.0", "entry": "index.js" }""";

    [Fact]
    public void CompleteManifestIsValid()
    {
        Assert.Empty(Check(Valid));
    }

    [Fact]
    public void MinimalManifestIsValid()
    {
        Assert.Empty(Check(Minimal));
    }

    [Fact]
    public void ManifestMustBeAnObject()
    {
        var error = Assert.Single(Check("[]"));

        Assert.Equal((string.Empty, FieldErrorCodes.InvalidFormat), (error.Field, error.Code));
    }

    [Fact]
    public void MissingFieldsAreRequired()
    {
        var errors = Check("{}");

        Assert.Equal(
            ["coreApi", "entry", "id", "name", "version"],
            errors.Where(error => error.Code == FieldErrorCodes.Required).Select(error => error.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnknownFieldIsNotAllowed()
    {
        AssertError(With("author", "Somebody"), "author", FieldErrorCodes.NotAllowed);
        AssertError(With("panels", JsonNode.Parse("""[{ "id": "stats", "slot": "analysis.tabs", "title": "Stats", "height": 300 }]""")), "panels[0].height", FieldErrorCodes.NotAllowed);
    }

    [Theory]
    [InlineData("id", "\"Card_Scan\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("id", "\"-scan\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("id", "\"a\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("id", "42", FieldErrorCodes.InvalidFormat)]
    [InlineData("kind", "\"module\"", FieldErrorCodes.NotAllowed)]
    [InlineData("name", "\"  \"", FieldErrorCodes.Required)]
    [InlineData("name", "null", FieldErrorCodes.InvalidFormat)]
    [InlineData("version", "\"1.0\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("version", "\"1.0.0+build.5\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("version", "\"01.0.0\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("version", "\"99999999999.0.0\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("coreApi", "\"latest\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("coreApi", "\"\"", FieldErrorCodes.Required)]
    [InlineData("entry", "\"../index.js\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("entry", "\"/index.js\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("entry", "\"dist/.hidden.js\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("entry", "\"dist/index.ts\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("entry", "\"https://cdn.example.com/index.js\"", FieldErrorCodes.InvalidFormat)]
    [InlineData("settings", "[]", FieldErrorCodes.InvalidFormat)]
    [InlineData("description", "1", FieldErrorCodes.InvalidFormat)]
    public void IncorrectTopLevelFieldGivesAnError(string field, string json, string code)
    {
        AssertError(With(field, JsonNode.Parse(json)), field, code);
    }

    [Fact]
    public void TooLongTextsAreRefused()
    {
        AssertError(With("name", new string('a', 101)), "name", FieldErrorCodes.TooLong);
        AssertError(With("description", new string('a', 501)), "description", FieldErrorCodes.TooLong);
        AssertError(With("entry", new string('a', 198) + ".js"), "entry", FieldErrorCodes.TooLong);
    }

    [Theory]
    [InlineData("permissions", "[\"decks:delete\"]", "permissions[0]", FieldErrorCodes.NotAllowed)]
    [InlineData("permissions", "[\"decks:read\", \"decks:read\"]", "permissions[1]", FieldErrorCodes.NotAllowed)]
    [InlineData("permissions", "\"decks:read\"", "permissions", FieldErrorCodes.InvalidFormat)]
    [InlineData("slots", "[\"Deck Actions\"]", "slots[0]", FieldErrorCodes.InvalidFormat)]
    [InlineData("slots", "[\"deck\"]", "slots[0]", FieldErrorCodes.InvalidFormat)]
    [InlineData("devices", "[\"microphone\"]", "devices[0]", FieldErrorCodes.NotAllowed)]
    [InlineData("devices", "[\"camera\", \"camera\"]", "devices[1]", FieldErrorCodes.NotAllowed)]
    public void IncorrectListItemGivesAnError(string field, string json, string errorField, string code)
    {
        AssertError(With(field, JsonNode.Parse(json), Minimal), errorField, code);
    }

    [Theory]
    [InlineData("http://api.example.com")]
    [InlineData("https://*.com")]
    [InlineData("https://*.*.example.com")]
    [InlineData("https://api.*.example.com")]
    [InlineData("*")]
    [InlineData("https:")]
    [InlineData("https://api.example.com/path")]
    [InlineData("https://API.example.com")]
    [InlineData("https://10.0.0.1")]
    [InlineData("http://[::1]")]
    [InlineData("https://example.com'; script-src *")]
    [InlineData("https://example.com https://other.org")]
    [InlineData("wss://example.com")]
    [InlineData("data:")]
    public void IncorrectNetworkOriginIsRefused(string origin)
    {
        AssertError(With("network", new JsonArray(origin)), "network[0]", FieldErrorCodes.InvalidFormat);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://api.example.co.uk:8443")]
    [InlineData("https://*.example.com")]
    [InlineData("http://localhost")]
    [InlineData("https://localhost:3000")]
    [InlineData("http://127.0.0.1:8080")]
    public void CorrectNetworkOriginIsAccepted(string origin)
    {
        Assert.Empty(Check(With("network", new JsonArray(origin))));
    }

    [Fact]
    public void TooManyItemsAreRefused()
    {
        var slots = new JsonArray(Enumerable.Range(0, 21).Select(index => (JsonNode)$"area.slot-{index}").ToArray());

        AssertError(With("slots", slots, Minimal), "slots", FieldErrorCodes.TooLong);
    }

    [Theory]
    [InlineData("""[{ "site": "Moxfield", "auth": "api-key" }]""", "accounts[0].site", FieldErrorCodes.InvalidFormat)]
    [InlineData("""[{ "site": "moxfield.com", "auth": "password" }]""", "accounts[0].auth", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "site": "moxfield.com" }]""", "accounts[0].auth", FieldErrorCodes.Required)]
    [InlineData("""[{ "site": "moxfield.com", "auth": "api-key" }, { "site": "moxfield.com", "auth": "public-username" }]""", "accounts[1].site", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "site": "moxfield.com", "auth": "api-key", "sync": "on-open" }]""", "accounts[0].sync", FieldErrorCodes.InvalidFormat)]
    [InlineData("""[{ "site": "moxfield.com", "auth": "api-key", "sync": {} }]""", "accounts[0].sync.mode", FieldErrorCodes.Required)]
    [InlineData("""[{ "site": "moxfield.com", "auth": "api-key", "sync": { "mode": "hourly" } }]""", "accounts[0].sync.mode", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "site": "moxfield.com", "auth": "api-key", "sync": { "mode": "periodic", "intervalMinutes": 5 } }]""", "accounts[0].sync.intervalMinutes", FieldErrorCodes.OutOfRange)]
    [InlineData("""[{ "site": "moxfield.com", "auth": "api-key", "sync": { "mode": "periodic", "intervalMinutes": 15.5 } }]""", "accounts[0].sync.intervalMinutes", FieldErrorCodes.InvalidFormat)]
    [InlineData("""["moxfield.com"]""", "accounts[0]", FieldErrorCodes.InvalidFormat)]
    public void IncorrectAccountGivesAnError(string json, string field, string code)
    {
        AssertError(With("accounts", JsonNode.Parse(json)), field, code);
    }

    [Theory]
    [InlineData("""[{ "id": "Stats", "slot": "analysis.tabs", "title": "Stats" }]""", "panels[0].id", FieldErrorCodes.InvalidFormat)]
    [InlineData("""[{ "id": "stats", "slot": "deck.actions", "title": "Stats" }]""", "panels[0].slot", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "id": "stats", "slot": "card.page", "title": "Stats" }]""", "panels[0].slot", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "id": "stats", "slot": "analysis.tabs", "title": "" }]""", "panels[0].title", FieldErrorCodes.Required)]
    [InlineData("""[{ "id": "stats", "slot": "analysis.tabs" }]""", "panels[0].title", FieldErrorCodes.Required)]
    [InlineData("""[{ "id": "stats", "slot": "analysis.tabs", "title": "A" }, { "id": "stats", "slot": "plugin.page", "title": "B" }]""", "panels[1].id", FieldErrorCodes.NotAllowed)]
    public void IncorrectPanelGivesAnError(string json, string field, string code)
    {
        AssertError(With("panels", JsonNode.Parse(json)), field, code);
    }

    [Fact]
    public void PanelTitleHasAMaximumLength()
    {
        var panels = new JsonArray(new JsonObject { ["id"] = "stats", ["slot"] = "analysis.tabs", ["title"] = new string('a', 61) });

        AssertError(With("panels", panels), "panels[0].title", FieldErrorCodes.TooLong);
    }

    [Theory]
    [InlineData("""[{ "id": "dek", "direction": "both", "target": "deck", "title": "Dek" }]""", "formats[0].direction", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "id": "dek", "direction": "import", "target": "card", "title": "Dek" }]""", "formats[0].target", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "id": "dek", "direction": "import", "target": "deck" }]""", "formats[0].title", FieldErrorCodes.Required)]
    [InlineData("""[{ "id": "dek", "direction": "import", "target": "deck", "title": "Dek", "extensions": ["dek"] }]""", "formats[0].extensions[0]", FieldErrorCodes.InvalidFormat)]
    [InlineData("""[{ "id": "dek", "direction": "import", "target": "deck", "title": "Dek", "extensions": [".dek", ".dek"] }]""", "formats[0].extensions[1]", FieldErrorCodes.NotAllowed)]
    [InlineData("""[{ "id": "dek", "direction": "import", "target": "deck", "title": "A" }, { "id": "dek", "direction": "export", "target": "deck", "title": "B" }]""", "formats[1].id", FieldErrorCodes.NotAllowed)]
    public void IncorrectFormatGivesAnError(string json, string field, string code)
    {
        AssertError(With("formats", JsonNode.Parse(json)), field, code);
    }

    [Theory]
    [InlineData("""{ "French": { "name": "Scan" } }""", "i18n.French", FieldErrorCodes.InvalidFormat)]
    [InlineData("""{ "fr": "Scan" }""", "i18n.fr", FieldErrorCodes.InvalidFormat)]
    [InlineData("""{ "fr": { "Name": "Scan" } }""", "i18n.fr.Name", FieldErrorCodes.InvalidFormat)]
    [InlineData("""{ "fr": { "panels..title": "Scan" } }""", "i18n.fr.panels..title", FieldErrorCodes.InvalidFormat)]
    [InlineData("""{ "fr": { "name": 1 } }""", "i18n.fr.name", FieldErrorCodes.InvalidFormat)]
    [InlineData("""[]""", "i18n", FieldErrorCodes.InvalidFormat)]
    public void IncorrectTranslationGivesAnError(string json, string field, string code)
    {
        AssertError(With("i18n", JsonNode.Parse(json)), field, code);
    }

    [Fact]
    public void TranslationsHaveLimits()
    {
        var longText = new JsonObject { ["fr"] = new JsonObject { ["name"] = new string('a', 201) } };
        var manyLocales = new JsonObject();
        foreach (var code in new[] { "aa", "bb", "cc", "dd", "ee", "ff", "gg", "hh", "ii", "jj", "kk" })
        {
            manyLocales[code] = new JsonObject();
        }

        AssertError(With("i18n", longText), "i18n.fr.name", FieldErrorCodes.TooLong);
        AssertError(With("i18n", manyLocales), "i18n", FieldErrorCodes.TooLong);
    }

    [Fact]
    public void EachErrorHasAMessage()
    {
        var errors = Check("""{ "id": "X", "kind": 1, "version": "x", "coreApi": 2, "entry": "..", "slots": [1], "network": [false], "devices": [null], "extra": true }""");

        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
    }

    private static string With(string field, JsonNode? value, string manifestJson = Valid)
    {
        var manifest = JsonNode.Parse(manifestJson)!.AsObject();
        manifest[field] = value;
        return manifest.ToJsonString();
    }

    private static IReadOnlyList<FieldError> Check(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PluginManifestCheck.Check(document.RootElement);
    }

    private static void AssertError(string json, string field, string code)
    {
        var errors = Check(json);

        Assert.Contains(errors, error => error.Field == field && error.Code == code);
        Assert.All(errors, error => Assert.Equal(field, error.Field));
    }
}
