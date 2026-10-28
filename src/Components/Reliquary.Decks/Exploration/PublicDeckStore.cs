using Dapper;
using Npgsql;

namespace Reliquary.Decks.Exploration;

/// <summary>
/// A public deck as the read model stores it.
/// </summary>
internal sealed class PublicDeckRow
{
    public Guid DeckId { get; set; }

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ColorIdentity { get; set; } = string.Empty;

    public Guid[] CommanderIds { get; set; } = [];

    public Guid[] CardIds { get; set; } = [];

    public int CardCount { get; set; }

    public int DeckVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int CopyCount { get; set; }

    public int FavoriteCount { get; set; }
}

/// <summary>
/// The filters, the sort and the page of the search of public decks.
/// </summary>
/// <param name="Text">A part of the deck name, or <see langword="null"/>.</param>
/// <param name="Format">The format, or <see langword="null"/>.</param>
/// <param name="ColorIdentity">The exact color identity, or <see langword="null"/>.</param>
/// <param name="CommanderId">The Oracle ID of a commander of the deck, or <see langword="null"/>.</param>
/// <param name="CardId">The Oracle ID of a card of the deck, or <see langword="null"/>.</param>
/// <param name="AuthorIds">Only the decks of these authors, or <see langword="null"/>.</param>
/// <param name="FavoriteOf">Only the favorite decks of this user, or <see langword="null"/>.</param>
/// <param name="Sort"><c>recent</c> or <c>popular</c>.</param>
/// <param name="Offset">The number of decks before the page.</param>
/// <param name="Limit">The maximum number of decks.</param>
internal sealed record PublicDeckQuery(
    string? Text,
    string? Format,
    string? ColorIdentity,
    Guid? CommanderId,
    Guid? CardId,
    IReadOnlyCollection<Guid>? AuthorIds,
    Guid? FavoriteOf,
    string Sort,
    int Offset,
    int Limit);

/// <summary>
/// The SQL of the read model of the public decks, in the users database.
/// </summary>
internal static class PublicDeckStore
{
    private const string Columns = """
        p.deck_id as DeckId, p.owner_id as OwnerId, p.name, p.format, p.description, p.color_identity as ColorIdentity,
        p.commander_ids as CommanderIds, p.card_ids as CardIds, p.card_count as CardCount, p.deck_version as DeckVersion,
        p.created_at as CreatedAt, p.updated_at as UpdatedAt,
        (select count(*) from users.public_deck_copies c where c.source_deck_id = p.deck_id)::int as CopyCount,
        (select count(*) from users.deck_favorites f where f.deck_id = p.deck_id)::int as FavoriteCount
        """;

    /// <summary>
    /// Writes a public deck. A row with a more recent deck version does not change.
    /// A hidden deck, or a deck of a hidden author, gets no row.
    /// </summary>
    public static Task UpsertAsync(NpgsqlConnection connection, PublicDeckRow row, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.public_decks (deck_id, owner_id, name, format, description, color_identity, commander_ids, card_ids, card_count, deck_version, created_at, updated_at)
            select @DeckId, @OwnerId, @Name, @Format, @Description, @ColorIdentity, @CommanderIds, @CardIds, @CardCount, @DeckVersion, @CreatedAt, @UpdatedAt
            where not exists (select 1 from users.moderation_hidden h
                where (h.item_type = 'deck' and h.item_id = @DeckId) or (h.item_type = 'author' and h.item_id = @OwnerId))
            on conflict (deck_id) do update set
                name = excluded.name, format = excluded.format, description = excluded.description, color_identity = excluded.color_identity,
                commander_ids = excluded.commander_ids, card_ids = excluded.card_ids, card_count = excluded.card_count,
                deck_version = excluded.deck_version, updated_at = excluded.updated_at
            where users.public_decks.deck_version <= excluded.deck_version
            """,
            row,
            cancellationToken: cancellationToken));

    public static Task DeleteAsync(NpgsqlConnection connection, Guid deckId, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition("delete from users.public_decks where deck_id = @deckId", new { deckId }, cancellationToken: cancellationToken));

    /// <summary>
    /// Removes a deleted deck: its row, its favorites and its copy links.
    /// </summary>
    public static Task DeleteDeckDataAsync(NpgsqlConnection connection, Guid deckId, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            delete from users.public_decks where deck_id = @deckId;
            delete from users.deck_favorites where deck_id = @deckId;
            delete from users.public_deck_copies where copy_deck_id = @deckId;
            """,
            new { deckId },
            cancellationToken: cancellationToken));

    public static Task AddCopyAsync(NpgsqlConnection connection, Guid copyDeckId, Guid sourceDeckId, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "insert into users.public_deck_copies (copy_deck_id, source_deck_id) values (@copyDeckId, @sourceDeckId) on conflict do nothing",
            new { copyDeckId, sourceDeckId },
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<PublicDeckRow>> SearchAsync(NpgsqlConnection connection, PublicDeckQuery query, CancellationToken cancellationToken)
    {
        var order = query.Sort == "popular"
            ? "(select count(*) from users.public_deck_copies c where c.source_deck_id = p.deck_id) + (select count(*) from users.deck_favorites f where f.deck_id = p.deck_id) desc, p.updated_at desc"
            : "p.updated_at desc";
        return (await connection.QueryAsync<PublicDeckRow>(new CommandDefinition(
            $"""
            select {Columns}
            from users.public_decks p
            where (@text::text is null or p.name ilike '%' || @text || '%')
                and (@format::text is null or p.format = @format)
                and (@colorIdentity::text is null or p.color_identity = @colorIdentity)
                and (@commanderId::uuid is null or p.commander_ids @> array[@commanderId::uuid])
                and (@cardId::uuid is null or p.card_ids @> array[@cardId::uuid])
                and (@hasAuthors = false or p.owner_id = any(@authorIds))
                and (@favoriteOf::uuid is null or exists (select 1 from users.deck_favorites f where f.deck_id = p.deck_id and f.account_id = @favoriteOf))
            order by {order}, p.deck_id
            offset @offset
            limit @limit
            """,
            new
            {
                text = query.Text,
                format = query.Format,
                colorIdentity = query.ColorIdentity,
                commanderId = query.CommanderId,
                cardId = query.CardId,
                hasAuthors = query.AuthorIds is not null,
                authorIds = query.AuthorIds?.ToArray() ?? [],
                favoriteOf = query.FavoriteOf,
                offset = query.Offset,
                limit = query.Limit,
            },
            cancellationToken: cancellationToken))).ToList();
    }

    public static Task<PublicDeckRow?> FindAsync(NpgsqlConnection connection, Guid deckId, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<PublicDeckRow>(new CommandDefinition(
            $"select {Columns} from users.public_decks p where p.deck_id = @deckId",
            new { deckId },
            cancellationToken: cancellationToken));

    public static Task<int> SetFavoriteAsync(NpgsqlConnection connection, Guid accountId, Guid deckId, bool favorite, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            favorite
                ? "insert into users.deck_favorites (account_id, deck_id, created_at) values (@accountId, @deckId, @now) on conflict do nothing"
                : "delete from users.deck_favorites where account_id = @accountId and deck_id = @deckId",
            new { accountId, deckId, now },
            cancellationToken: cancellationToken));

    public static Task<bool> IsFavoriteAsync(NpgsqlConnection connection, Guid accountId, Guid deckId, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from users.deck_favorites where account_id = @accountId and deck_id = @deckId)",
            new { accountId, deckId },
            cancellationToken: cancellationToken));

    public static Task SetFollowAsync(NpgsqlConnection connection, Guid followerId, Guid authorId, bool follow, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            follow
                ? "insert into users.author_follows (follower_id, author_id, created_at) values (@followerId, @authorId, @now) on conflict do nothing"
                : "delete from users.author_follows where follower_id = @followerId and author_id = @authorId",
            new { followerId, authorId, now },
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<Guid>> ListFollowedAsync(NpgsqlConnection connection, Guid followerId, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            select f.author_id from users.author_follows f
            where f.follower_id = @followerId
                and not exists (select 1 from users.moderation_hidden h where h.item_type = 'author' and h.item_id = f.author_id)
            order by f.created_at
            """,
            new { followerId },
            cancellationToken: cancellationToken))).ToList();

    public static Task<(int Decks, int Followers)> CountAuthorAsync(NpgsqlConnection connection, Guid authorId, CancellationToken cancellationToken) =>
        connection.QuerySingleAsync<(int, int)>(new CommandDefinition(
            """
            select (select count(*) from users.public_decks where owner_id = @authorId)::int,
                (select count(*) from users.author_follows where author_id = @authorId)::int
            """,
            new { authorId },
            cancellationToken: cancellationToken));

    public static Task<bool> IsFollowingAsync(NpgsqlConnection connection, Guid followerId, Guid authorId, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from users.author_follows where follower_id = @followerId and author_id = @authorId)",
            new { followerId, authorId },
            cancellationToken: cancellationToken));
}
