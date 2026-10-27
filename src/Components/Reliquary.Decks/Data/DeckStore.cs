using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Decks.Data;

/// <summary>
/// A deck as the table <c>content.decks</c> stores it.
/// </summary>
internal sealed class DeckRow
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Visibility { get; set; } = "private";

    public Guid? FolderId { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? SourceDeckId { get; set; }

    public bool Pinned { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A deck with the number of cards in its main zones, for the deck list.
/// </summary>
internal sealed class DeckSummaryRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Visibility { get; set; } = "private";

    public Guid? FolderId { get; set; }

    public bool Pinned { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public int CardCount { get; set; }

    public Guid[] CommanderIds { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A line of the working list of a deck.
/// </summary>
internal sealed class DeckLineRow
{
    public Guid Id { get; set; }

    public Guid DeckId { get; set; }

    public Guid OracleId { get; set; }

    public Guid? PrintingId { get; set; }

    public string? Finish { get; set; }

    public int Quantity { get; set; }

    public string Zone { get; set; } = "main";

    public string Note { get; set; } = string.Empty;

    public string[] Categories { get; set; } = [];

    public Guid[] TagIds { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets the content of the line, to find the lines that a change modified.</summary>
    public string Content => $"{PrintingId}|{Finish}|{Quantity}|{Zone}|{Note}|{string.Join(',', Categories.Order(StringComparer.Ordinal))}|{string.Join(',', TagIds.Order())}";

    /// <summary>Gets the key of the line: one line for each card, printing and finish in a zone.</summary>
    public (string Zone, Guid OracleId, Guid? PrintingId, string? Finish) Key => (Zone, OracleId, PrintingId, Finish);
}

/// <summary>
/// A tag of a user: a global tag, or a local tag of one deck.
/// </summary>
internal sealed class TagRow
{
    public Guid Id { get; set; }

    public Guid? DeckId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsGlobal => DeckId is null;
}

/// <summary>
/// Reads and writes the decks in the shards of their owners, and the deck directory in the <c>users</c> schema.
/// </summary>
/// <param name="database">The databases of the instance.</param>
internal sealed class DeckStore(IDatabase database)
{
    private const string DeckColumns = """
        id, owner_id as OwnerId, name, format, visibility, folder_id as FolderId, description, source_deck_id as SourceDeckId,
        pinned, archived_at as ArchivedAt, version, created_at as CreatedAt, updated_at as UpdatedAt
        """;

    private const string LineColumns = """
        l.id, l.deck_id as DeckId, l.oracle_id as OracleId, l.printing_id as PrintingId, l.finish, l.quantity, l.zone, l.note, l.categories,
        coalesce((select array_agg(t.tag_id order by t.tag_id) from content.deck_line_tags t where t.deck_line_id = l.id), '{}') as TagIds,
        l.created_at as CreatedAt, l.updated_at as UpdatedAt
        """;

    public ValueTask<NpgsqlConnection> OpenAsync(Guid ownerId, ContentAccess access, CancellationToken cancellationToken) =>
        database.OpenContentAsync(ownerId, access, cancellationToken);

    /// <summary>
    /// Finds the owner of a deck in the deck directory.
    /// </summary>
    /// <returns>The owner, or <see langword="null"/> when the deck does not exist.</returns>
    public async Task<Guid?> FindOwnerAsync(Guid deckId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "select owner_id from users.deck_directory where deck_id = @deckId",
            new { deckId },
            cancellationToken: cancellationToken));
    }

    public async Task AddToDirectoryAsync(Guid deckId, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "insert into users.deck_directory (deck_id, owner_id) values (@deckId, @ownerId) on conflict (deck_id) do nothing",
            new { deckId, ownerId },
            cancellationToken: cancellationToken));
    }

    public async Task RemoveFromDirectoryAsync(Guid deckId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenUsersAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from users.deck_directory where deck_id = @deckId",
            new { deckId },
            cancellationToken: cancellationToken));
    }

    public static Task<DeckRow?> FindDeckAsync(NpgsqlConnection connection, Guid ownerId, Guid deckId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null, bool forUpdate = false) =>
        connection.QuerySingleOrDefaultAsync<DeckRow>(new CommandDefinition(
            $"select {DeckColumns} from content.decks where id = @deckId and owner_id = @ownerId{(forUpdate ? " for update" : string.Empty)}",
            new { deckId, ownerId },
            transaction,
            cancellationToken: cancellationToken));

    public static async Task<IReadOnlyList<DeckLineRow>> ListLinesAsync(NpgsqlConnection connection, Guid deckId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        (await connection.QueryAsync<DeckLineRow>(new CommandDefinition(
            $"select {LineColumns} from content.deck_lines l where l.deck_id = @deckId order by l.created_at, l.id",
            new { deckId },
            transaction,
            cancellationToken: cancellationToken))).ToList();

    public static async Task<IReadOnlyList<DeckSummaryRow>> ListDecksAsync(NpgsqlConnection connection, Guid ownerId, (DateTime UpdatedAt, Guid Id)? after, int limit, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<DeckSummaryRow>(new CommandDefinition(
            """
            select d.id, d.name, d.format, d.visibility, d.folder_id as FolderId, d.pinned, d.archived_at as ArchivedAt,
                d.created_at as CreatedAt, d.updated_at as UpdatedAt,
                coalesce((select sum(l.quantity) from content.deck_lines l where l.deck_id = d.id and l.zone in ('commander', 'companion', 'main')), 0)::int as CardCount,
                coalesce((select array_agg(l.oracle_id order by l.created_at) from content.deck_lines l where l.deck_id = d.id and l.zone = 'commander'), '{}') as CommanderIds
            from content.decks d
            where d.owner_id = @ownerId
                and (@hasCursor = false or (d.updated_at, d.id) < (@afterUpdatedAt, @afterId))
            order by d.updated_at desc, d.id desc
            limit @limit
            """,
            new { ownerId, hasCursor = after is not null, afterUpdatedAt = after?.UpdatedAt ?? DateTime.MinValue, afterId = after?.Id ?? Guid.Empty, limit },
            cancellationToken: cancellationToken))).ToList();

    public static Task InsertDeckAsync(NpgsqlTransaction transaction, DeckRow deck, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into content.decks (id, owner_id, name, format, visibility, folder_id, description, source_deck_id, pinned, archived_at, version, created_at, updated_at)
            values (@Id, @OwnerId, @Name, @Format, @Visibility, @FolderId, @Description, @SourceDeckId, @Pinned, @ArchivedAt, @Version, @CreatedAt, @UpdatedAt)
            """,
            deck,
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Writes the fields of a deck and increases its version.
    /// </summary>
    public static Task UpdateDeckAsync(NpgsqlTransaction transaction, DeckRow deck, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            update content.decks
            set name = @Name, format = @Format, visibility = @Visibility, folder_id = @FolderId, description = @Description,
                pinned = @Pinned, archived_at = @ArchivedAt, version = @Version, updated_at = @UpdatedAt
            where id = @Id and owner_id = @OwnerId
            """,
            deck,
            transaction,
            cancellationToken: cancellationToken));

    public static Task DeleteDeckAsync(NpgsqlTransaction transaction, Guid ownerId, Guid deckId, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "delete from content.decks where id = @deckId and owner_id = @ownerId",
            new { deckId, ownerId },
            transaction,
            cancellationToken: cancellationToken));

    public static Task InsertLinesAsync(NpgsqlTransaction transaction, Guid ownerId, IEnumerable<DeckLineRow> lines, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into content.deck_lines (id, deck_id, owner_id, oracle_id, printing_id, finish, quantity, zone, note, categories, created_at, updated_at)
            values (@Id, @DeckId, @OwnerId, @OracleId, @PrintingId, @Finish, @Quantity, @Zone, @Note, @Categories, @CreatedAt, @UpdatedAt)
            """,
            lines.Select(line => new
            {
                line.Id,
                line.DeckId,
                OwnerId = ownerId,
                line.OracleId,
                line.PrintingId,
                line.Finish,
                line.Quantity,
                line.Zone,
                line.Note,
                line.Categories,
                line.CreatedAt,
                line.UpdatedAt,
            }),
            transaction,
            cancellationToken: cancellationToken));

    public static Task InsertLineTagsAsync(NpgsqlTransaction transaction, IEnumerable<DeckLineRow> lines, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "insert into content.deck_line_tags (deck_line_id, tag_id) values (@LineId, @TagId) on conflict do nothing",
            lines.SelectMany(line => line.TagIds.Select(tagId => new { LineId = line.Id, TagId = tagId })),
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Gets the global tags of an owner and the local tags of a deck.
    /// </summary>
    public static async Task<IReadOnlyList<TagRow>> ListTagsAsync(NpgsqlConnection connection, Guid ownerId, Guid? deckId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        (await connection.QueryAsync<TagRow>(new CommandDefinition(
            """
            select id, deck_id as DeckId, name from content.tags
            where owner_id = @ownerId and (deck_id is null or deck_id = @deckId)
            order by deck_id nulls first, lower(name)
            """,
            new { ownerId, deckId },
            transaction,
            cancellationToken: cancellationToken))).ToList();

    public static async Task<IReadOnlyList<TagRow>> FindTagsAsync(NpgsqlConnection connection, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<TagRow>(new CommandDefinition(
            "select id, deck_id as DeckId, name from content.tags where id = any(@ids)",
            new { ids = tagIds.ToArray() },
            cancellationToken: cancellationToken))).ToList();

    public static Task InsertTagsAsync(NpgsqlTransaction transaction, Guid ownerId, IEnumerable<TagRow> tags, DateTime now, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "insert into content.tags (id, owner_id, deck_id, name, created_at, updated_at) values (@Id, @OwnerId, @DeckId, @Name, @Now, @Now)",
            tags.Select(tag => new { tag.Id, OwnerId = ownerId, tag.DeckId, tag.Name, Now = now }),
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Gets the global tags of an owner with the number of deck lines that use each tag.
    /// </summary>
    public static async Task<IReadOnlyList<(Guid Id, string Name, int LineCount)>> ListGlobalTagsAsync(NpgsqlConnection connection, Guid ownerId, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<(Guid Id, string Name, int LineCount)>(new CommandDefinition(
            """
            select t.id, t.name, (select count(*) from content.deck_line_tags l where l.tag_id = t.id)::int
            from content.tags t
            where t.owner_id = @ownerId and t.deck_id is null
            order by lower(t.name)
            """,
            new { ownerId },
            cancellationToken: cancellationToken))).ToList();

    public static Task<TagRow?> FindOwnedTagAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ownerId, Guid tagId, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<TagRow>(new CommandDefinition(
            "select id, deck_id as DeckId, name from content.tags where id = @tagId and owner_id = @ownerId for update",
            new { tagId, ownerId },
            transaction,
            cancellationToken: cancellationToken));

    public static Task<TagRow?> FindTagByNameAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ownerId, Guid? deckId, string name, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<TagRow>(new CommandDefinition(
            """
            select id, deck_id as DeckId, name from content.tags
            where owner_id = @ownerId and deck_id is not distinct from @deckId and lower(name) = lower(@name)
            """,
            new { ownerId, deckId, name },
            transaction,
            cancellationToken: cancellationToken));

    public static Task RenameTagAsync(NpgsqlTransaction transaction, Guid tagId, string name, DateTime now, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "update content.tags set name = @name, updated_at = @now where id = @tagId",
            new { tagId, name, now },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Moves the deck lines of one tag to another tag, then deletes the first tag.
    /// </summary>
    public static async Task MergeTagAsync(NpgsqlTransaction transaction, Guid fromTagId, Guid intoTagId, CancellationToken cancellationToken)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            """
            insert into content.deck_line_tags (deck_line_id, tag_id)
            select deck_line_id, @intoTagId from content.deck_line_tags where tag_id = @fromTagId
            on conflict do nothing
            """,
            new { fromTagId, intoTagId },
            transaction,
            cancellationToken: cancellationToken));
        await DeleteTagAsync(transaction, fromTagId, cancellationToken);
    }

    public static Task DeleteTagAsync(NpgsqlTransaction transaction, Guid tagId, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "delete from content.tags where id = @tagId",
            new { tagId },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Gets the decks that contain a line with a tag, and increases their versions.
    /// </summary>
    public static async Task<IReadOnlyList<DeckRow>> TouchDecksOfTagAsync(NpgsqlTransaction transaction, Guid tagId, DateTime now, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<DeckRow>(new CommandDefinition(
            $"""
            update content.decks d set version = version + 1, updated_at = @now
            where d.id in (select l.deck_id from content.deck_lines l join content.deck_line_tags t on t.deck_line_id = l.id where t.tag_id = @tagId)
            returning {DeckColumns}
            """,
            new { tagId, now },
            transaction,
            cancellationToken: cancellationToken))).ToList();

    public static Task DeleteLinesAsync(NpgsqlTransaction transaction, Guid deckId, IEnumerable<Guid> lineIds, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "delete from content.deck_lines where deck_id = @deckId and id = any(@ids)",
            new { deckId, ids = lineIds.ToArray() },
            transaction,
            cancellationToken: cancellationToken));

    public static Task<bool> FolderExistsAsync(NpgsqlConnection connection, Guid ownerId, Guid folderId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from content.deck_folders where id = @folderId and owner_id = @ownerId)",
            new { folderId, ownerId },
            transaction,
            cancellationToken: cancellationToken));
}
