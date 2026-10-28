using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Collection.Api;

namespace Reliquary.Collection.Tests;

/// <summary>
/// Helpers for the requests of the collection tests.
/// </summary>
internal static class CollectionApi
{
    public static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static async Task<LocationResponse> CreateLocationAsync(this HttpClient client, string name, string? kind = "rack", Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/collection/locations", new { name, kind, parentId }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<LocationResponse>(Cancel))!;
    }

    public static async Task<EntriesChangeResponse> AddAsync(this HttpClient client, Guid locationId, params object[] entries)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/collection/locations/{locationId}/entries", new { entries }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<EntriesChangeResponse>(Cancel))!;
    }

    public static async Task<List<EntryResponse>> EntriesAsync(this HttpClient client, Guid locationId, string query = "")
    {
        var page = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/collection/locations/{locationId}/entries?limit=100{query}", UriKind.Relative), Cancel);
        return page.GetProperty("items").Deserialize<List<EntryResponse>>(JsonSerializerOptions.Web)!;
    }

    public static async Task<string> CodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        return problem.GetProperty("code").GetString()!;
    }

    public static async Task<string?> FieldAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        return problem.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0 ? errors[0].GetProperty("field").GetString() : null;
    }

    public static async Task<Guid> AccountIdAsync(this HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/me", UriKind.Relative), Cancel);
        return me.GetProperty("account").GetProperty("id").GetGuid();
    }

    /// <summary>Finds the Oracle ID of a card of the fixtures.</summary>
    public static async Task<Guid> OracleIdAsync(this CollectionInstance instance, string name)
    {
        var catalog = (await instance.GetAsync()).Services.GetRequiredService<ICardCatalog>();
        var match = (await catalog.ResolveNamesAsync([name], Cancel))[name];
        return match.OracleId!.Value;
    }

    /// <summary>Finds a printing of the fixtures by its set code and collector number.</summary>
    public static async Task<PrintingFacts> PrintingAsync(this CollectionInstance instance, string set, string number, string? lang = null)
    {
        var catalog = (await instance.GetAsync()).Services.GetRequiredService<ICardCatalog>();
        var reference = new PrintingReference(set, number, lang);
        return (await catalog.FindPrintingsAsync([reference], Cancel))[reference];
    }

    /// <summary>Counts the rows of a table of the content schema for an owner.</summary>
    public static async Task<long> CountAsync(this CollectionInstance instance, string table, Guid ownerId, string condition = "true")
    {
        await using var connection = new NpgsqlConnection(instance.ConnectionString);
        await connection.OpenAsync(Cancel);
        return await connection.ExecuteScalarAsync<long>($"select count(*) from content.{table} where owner_id = @ownerId and {condition}", new { ownerId });
    }
}
