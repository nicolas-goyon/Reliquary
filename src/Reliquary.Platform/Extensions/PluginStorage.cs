using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// The storage of the plugins: small JSON values by key, for one plugin and one user. See issue #279.
/// </summary>
/// <param name="database">The databases of the instance.</param>
/// <param name="time">The clock.</param>
internal sealed class PluginStorage(IDatabase database, TimeProvider time)
{
    /// <summary>Gets the keys of a plugin for an owner, in ordinal order.</summary>
    public async Task<IReadOnlyList<string>> KeysAsync(Guid ownerId, string pluginId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Read, cancellationToken);
        var keys = await connection.QueryAsync<string>(new CommandDefinition(
            "select key from content.plugin_storage where owner_id = @ownerId and extension_id = @pluginId",
            new { ownerId, pluginId },
            cancellationToken: cancellationToken));
        return keys.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Gets the JSON text of a value, or null when the plugin has no value for the key.</summary>
    public async Task<string?> GetAsync(Guid ownerId, string pluginId, string key, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "select value::text from content.plugin_storage where owner_id = @ownerId and extension_id = @pluginId and key = @key",
            new { ownerId, pluginId, key },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Keeps a value. The write and the check of the size are in one transaction.
    /// </summary>
    /// <returns>False when the storage of the plugin for the owner would contain more than <see cref="PluginStorageEndpoints.MaxBytes"/>. The storage then does not change.</returns>
    public async Task<bool> SetAsync(Guid ownerId, string pluginId, string key, string json, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var size = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            insert into content.plugin_storage (owner_id, extension_id, key, value, updated_at)
            values (@ownerId, @pluginId, @key, @json::jsonb, @now)
            on conflict (owner_id, extension_id, key) do update set value = excluded.value, updated_at = excluded.updated_at;

            select coalesce(sum(octet_length(key) + octet_length(value::text)), 0)
            from content.plugin_storage where owner_id = @ownerId and extension_id = @pluginId;
            """,
            new { ownerId, pluginId, key, json, now = time.GetUtcNow() },
            transaction,
            cancellationToken: cancellationToken));
        if (size > PluginStorageEndpoints.MaxBytes)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Deletes a value. The result is the same when the plugin has no value for the key.</summary>
    public async Task RemoveAsync(Guid ownerId, string pluginId, string key, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.plugin_storage where owner_id = @ownerId and extension_id = @pluginId and key = @key",
            new { ownerId, pluginId, key },
            cancellationToken: cancellationToken));
    }

    /// <summary>Gets all the values of an owner, for the export of the personal data.</summary>
    public async Task<IReadOnlyList<(string PluginId, string Key, string Json)>> ListAllAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Read, cancellationToken);
        var rows = await connection.QueryAsync<(string PluginId, string Key, string Json)>(new CommandDefinition(
            "select extension_id, key, value::text from content.plugin_storage where owner_id = @ownerId order by extension_id, key",
            new { ownerId },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }
}

/// <summary>The keys of the storage of a plugin.</summary>
/// <param name="Keys">The keys, in ordinal order.</param>
public sealed record PluginStorageKeysResponse(IReadOnlyList<string> Keys);

/// <summary>A value of the storage of a plugin.</summary>
/// <param name="Value">The JSON value.</param>
public sealed record PluginStorageValue(JsonElement? Value);

/// <summary>
/// The endpoints of the plugin storage. They accept only a plugin token of the plugin of the address.
/// </summary>
public static class PluginStorageEndpoints
{
    /// <summary>The storage of the plugin for the user is full.</summary>
    public const string StorageFull = "plugin_storage_full";

    /// <summary>The maximum size of the keys and the values of one plugin and one user, in bytes of UTF-8 text.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The maximum length of a key, in characters.</summary>
    public const int MaxKeyLength = 128;

    internal static void MapPluginStorageEndpoints(this IEndpointRouteBuilder api)
    {
        // The key is in the query, because a key can contain a slash.
        api.MapGet("/me/extensions/{id}/storage", GetAsync)
            .WithTags("Extensions")
            .WithName("GetPluginStorage")
            .WithSummary("Without the parameter key: lists the keys of the storage of the plugin for the user. With key: gives the value of the key. Only a plugin token of the plugin {id} can call it.")
            .RequirePluginToken();

        api.MapPut("/me/extensions/{id}/storage", SetAsync)
            .WithTags("Extensions")
            .WithName("SetPluginStorageValue")
            .WithSummary("Keeps a JSON value for the key of the parameter key. The storage of one plugin and one user has 64 KB at most.")
            .RequirePluginToken();

        api.MapDelete("/me/extensions/{id}/storage", RemoveAsync)
            .WithTags("Extensions")
            .WithName("RemovePluginStorageValue")
            .WithSummary("Deletes the value of the key of the parameter key. Only a plugin token of the plugin {id} can call it.")
            .RequirePluginToken();
    }

    private static async Task<Results<Ok<PluginStorageKeysResponse>, ContentHttpResult, ProblemHttpResult>> GetAsync(
        string id, string? key, ClaimsPrincipal user, PluginStorage storage, HttpContext http, CancellationToken cancellationToken)
    {
        if (Refuse(user, id, key, keyIsNecessary: false) is { } problem)
        {
            return problem;
        }

        http.Response.Headers.CacheControl = "no-store";
        if (key is null)
        {
            return TypedResults.Ok(new PluginStorageKeysResponse(await storage.KeysAsync(user.GetAccountId(), id, cancellationToken)));
        }

        if (await storage.GetAsync(user.GetAccountId(), id, key, cancellationToken) is not { } json)
        {
            return ApiProblems.NotFound("The storage of the plugin has no value for this key.");
        }

        return TypedResults.Content($$"""{"value":{{json}}}""", "application/json");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetAsync(
        string id, string? key, PluginStorageValue body, ClaimsPrincipal user, PluginStorage storage, CancellationToken cancellationToken)
    {
        if (Refuse(user, id, key, keyIsNecessary: true) is { } problem)
        {
            return problem;
        }

        if (body.Value is not { } value || value.ValueKind == JsonValueKind.Undefined)
        {
            return ApiProblems.Validation(new FieldError("value", FieldErrorCodes.Required, "The field \"value\" is necessary. To delete a value, use DELETE."));
        }

        var json = value.GetRawText();
        if (!await storage.SetAsync(user.GetAccountId(), id, key!, json, cancellationToken))
        {
            return ApiProblems.Problem(
                StatusCodes.Status413PayloadTooLarge,
                StorageFull,
                $"The storage of the plugin for this user can contain {MaxBytes / 1024} KB at most. Delete values, or keep a smaller value.");
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        string id, string? key, ClaimsPrincipal user, PluginStorage storage, CancellationToken cancellationToken)
    {
        if (Refuse(user, id, key, keyIsNecessary: true) is { } problem)
        {
            return problem;
        }

        await storage.RemoveAsync(user.GetAccountId(), id, key!, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Refuses the token of a different plugin, and a key that is not valid.</summary>
    private static ProblemHttpResult? Refuse(ClaimsPrincipal user, string id, string? key, bool keyIsNecessary)
    {
        if (user.GetPluginId() != id)
        {
            return ApiProblems.Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "The plugin token belongs to a different plugin. A plugin can use only its own storage.");
        }

        if (key is null ? keyIsNecessary : key.Length is 0 or > MaxKeyLength || key.Any(char.IsControl))
        {
            return ApiProblems.Validation(new FieldError("key", FieldErrorCodes.InvalidFormat, $"The parameter \"key\" must have 1 to {MaxKeyLength} characters, with no control characters."));
        }

        return null;
    }
}
