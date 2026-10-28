using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Api.Tests;

/// <summary>
/// Checks the contract of the internal API: its OpenAPI document and the catalog of the domain events.
/// See docs/architecture/internal-api.md.
/// </summary>
public sealed class InternalApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>The snapshot of the OpenAPI document of the internal API, relative to the repository root.</summary>
    public const string SnapshotPath = "docs/architecture/internal-api-v1.json";

    /// <summary>The documentation file with the catalog of the domain events, relative to the repository root.</summary>
    public const string EventDocumentPath = "docs/architecture/domain-events.md";

    private const string UpdateVariable = "RELIQUARY_UPDATE_SNAPSHOTS";
    private const string CatalogStart = "<!-- event-catalog:start -->";
    private const string CatalogEnd = "<!-- event-catalog:end -->";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InternalOpenApiDocumentMatchesTheSnapshot()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/internal-v1.json", UriKind.Relative), Cancel);
        var actual = Normalize(await response.Content.ReadAsStringAsync(Cancel));
        var path = Path.Combine(RepositoryRoot.Path, SnapshotPath);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, actual, Cancel);
        }

        var expected = File.Exists(path) ? Normalize(await File.ReadAllTextAsync(path, Cancel)) : string.Empty;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            expected == actual,
            $"The OpenAPI document of the internal API is different from {SnapshotPath}. " +
            $"When the change is correct, run the tests with {UpdateVariable}=1, then commit the file. Increase the core API version for a change that modules can see.");
    }

    [Fact]
    public async Task PublicOpenApiDocumentHasNoInternalEndpoint()
    {
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancel);

        Assert.DoesNotContain("/internal/", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InternalApiRefusesARequestWithoutCredential()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/internal/v1/events/types", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void EventCatalogContainsEachEventType()
    {
        var catalog = factory.Services.GetRequiredService<DomainEventCatalog>();

        Assert.Contains(catalog.Types, type => type is { Type: "accounts.account.created", Version: 1 });
        Assert.Contains(catalog.Types, type => type is { Type: "decks.deck.created", Version: 1 });
        Assert.Contains(catalog.Types, type => type is { Type: "cards.card_data.imported", Version: 1 });
        var created = catalog.Types.Single(type => type.Type == "decks.deck.created");
        Assert.Equal(new DomainEventField("sourceDeckId", "uuid", true), created.Fields.Single(field => field.Name == "sourceDeckId"));
    }

    [Fact]
    public async Task EventCatalogMatchesTheDocumentation()
    {
        var catalog = factory.Services.GetRequiredService<DomainEventCatalog>();
        var expected = CatalogTable(catalog);

        var text = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot.Path, EventDocumentPath), Cancel);
        var start = text.IndexOf(CatalogStart, StringComparison.Ordinal);
        var end = text.IndexOf(CatalogEnd, StringComparison.Ordinal);
        var documented = start >= 0 && end > start
            ? text[(start + CatalogStart.Length)..end].ReplaceLineEndings("\n").Trim()
            : string.Empty;

        Assert.True(
            expected == documented,
            $"The catalog in {EventDocumentPath} is different from the [DomainEvent] types. Put this table between the markers {CatalogStart} and {CatalogEnd}:\n\n{expected}\n");
    }

    /// <summary>
    /// Writes the catalog as a Markdown table, in the format of the documentation.
    /// </summary>
    private static string CatalogTable(DomainEventCatalog catalog)
    {
        var table = new StringBuilder();
        table.Append("| Type | Version | Payload |\n");
        table.Append("| --- | --- | --- |");
        foreach (var type in catalog.Types)
        {
            var fields = string.Join(", ", type.Fields.Select(field => $"`{field.Name}` ({field.Type}{(field.Nullable ? " or null" : string.Empty)})"));
            table.Append(CultureInfo.InvariantCulture, $"\n| `{type.Type}` | {type.Version} | {fields} |");
        }

        return table.ToString();
    }

    /// <summary>
    /// Removes the server addresses, which depend on the host, and indents the JSON with a final new line.
    /// </summary>
    private static string Normalize(string json)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        document.Remove("servers");
        return document.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
    }
}
