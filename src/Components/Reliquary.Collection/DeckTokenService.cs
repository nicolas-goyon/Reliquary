using Dapper;
using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// A token of an assembled deck, and whether the owner has it.
/// </summary>
internal sealed record DeckToken(Guid OracleId, string Name, bool Owned);

/// <summary>
/// The tokens of the assembled decks. <see cref="AssembledDeckLocations.SetContentAsync"/> gives the tokens of the deck.
/// The owner marks each token as owned or not.
/// </summary>
internal sealed class DeckTokenService(CollectionStore store, ICardCatalog catalog, TimeProvider time)
{
    public async Task<IReadOnlyList<DeckToken>> ListAsync(Guid ownerId, Guid locationId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        await DeckLocationAsync(connection, ownerId, locationId, cancellationToken);
        var rows = (await connection.QueryAsync<(Guid OracleId, bool Owned)>(new CommandDefinition(
            "select oracle_id, owned from content.deck_tokens where location_id = @locationId and owner_id = @ownerId",
            new { locationId, ownerId },
            cancellationToken: cancellationToken))).ToList();
        var cards = await catalog.GetCardsAsync([.. rows.Select(row => row.OracleId)], cancellationToken);
        return [.. rows
            .Select(row => new DeckToken(row.OracleId, cards.TryGetValue(row.OracleId, out var card) ? card.Name : string.Empty, row.Owned))
            .OrderBy(token => token.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Marks a token of an assembled deck as owned or not. A token that the deck does not list joins the list.
    /// </summary>
    public async Task SetOwnedAsync(Guid ownerId, Guid locationId, Guid oracleId, bool owned, CancellationToken cancellationToken)
    {
        var cards = await catalog.GetCardsAsync([oracleId], cancellationToken);
        if (!cards.ContainsKey(oracleId))
        {
            throw CollectionException.Validation(new FieldError("oracleId", FieldErrorCodes.NotAllowed, "The card does not exist."));
        }

        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await DeckLocationAsync(connection, ownerId, locationId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.deck_tokens (location_id, owner_id, oracle_id, owned, updated_at) values (@locationId, @ownerId, @oracleId, @owned, @now)
            on conflict (location_id, oracle_id) do update set owned = excluded.owned, updated_at = excluded.updated_at
            """,
            new { locationId, ownerId, oracleId, owned, now = time.GetUtcNow().UtcDateTime },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid ownerId, Guid locationId, Guid oracleId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await DeckLocationAsync(connection, ownerId, locationId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.deck_tokens where location_id = @locationId and owner_id = @ownerId and oracle_id = @oracleId",
            new { locationId, ownerId, oracleId },
            cancellationToken: cancellationToken));
    }

    private static async Task DeckLocationAsync(Npgsql.NpgsqlConnection connection, Guid ownerId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken) ?? throw CollectionException.LocationNotFound();
        if (location.DeckId is null)
        {
            throw new CollectionException(StatusCodes.Status409Conflict, ErrorCodes.Conflict, "The location is not an assembled deck. Only an assembled deck has tokens.");
        }
    }
}
