using Dapper;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Decks.Data;
using Reliquary.Decks.Exchange;
using Reliquary.Decks.Text;

namespace Reliquary.Decks;

/// <summary>
/// Gives the data of the Decks component for the export of an account, and deletes it for the deletion of an account.
/// </summary>
internal sealed class DeckPersonalData(DeckStore store, DeckService decks, DeckTextService texts, TagService tags, FolderService folders) : IPersonalDataProvider
{
    public string Name => "decks";

    public async Task ExportAsync(Guid ownerId, IPersonalDataArchive archive, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> deckIds;
        await using (var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken))
        {
            deckIds = [.. await connection.QueryAsync<Guid>(new CommandDefinition(
                "select id from content.decks where owner_id = @ownerId order by created_at, id", new { ownerId }, cancellationToken: cancellationToken))];
        }

        var exported = new List<object>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deckId in deckIds)
        {
            var view = await decks.GetAsync(ownerId, deckId, cancellationToken);
            var lines = await texts.ToTextLinesAsync(view, cancellationToken);
            var stem = DeckExchangeService.FileName(view.Deck.Name);
            var fileName = $"{stem}.txt";
            for (var number = 2; !usedNames.Add(fileName); number++)
            {
                fileName = $"{stem} ({number}).txt";
            }

            await archive.WriteTextAsync($"text/{fileName}", DeckText.Write(lines), cancellationToken);
            var deck = view.Deck;
            exported.Add(new
            {
                deck.Id,
                deck.Name,
                deck.Format,
                deck.Visibility,
                deck.FolderId,
                deck.Description,
                deck.SourceDeckId,
                deck.Pinned,
                Archived = deck.ArchivedAt is not null,
                deck.Tags,
                deck.Version,
                deck.CreatedAt,
                deck.UpdatedAt,
                TextFile = $"text/{fileName}",
                Lines = lines.Select(line => new
                {
                    line.Name,
                    line.Quantity,
                    line.Zone,
                    line.SetCode,
                    line.CollectorNumber,
                    line.Finish,
                    line.Tags,
                    line.GlobalTags,
                    line.Categories,
                    line.Note,
                }),
            });
        }

        await archive.WriteJsonAsync("decks.json", exported, cancellationToken);
        var folderRows = await folders.ListAsync(ownerId, cancellationToken);
        await archive.WriteJsonAsync("folders.json", folderRows.Select(folder => new { folder.Id, folder.ParentId, folder.Name }), cancellationToken);
        var globalTags = await tags.ListGlobalAsync(ownerId, cancellationToken);
        await archive.WriteJsonAsync("global-tags.json", globalTags.Select(tag => new { tag.Name, tag.LineCount }), cancellationToken);

        await using (var content = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken))
        {
            var rules = await content.QueryAsync<(Guid OracleId, string[] Categories, DateTime UpdatedAt)>(new CommandDefinition(
                "select oracle_id, categories, updated_at from content.category_rules where owner_id = @ownerId order by updated_at",
                new { ownerId },
                cancellationToken: cancellationToken));
            await archive.WriteJsonAsync("category-rules.json", rules.Select(rule => new { rule.OracleId, rule.Categories, rule.UpdatedAt }), cancellationToken);
        }

        await using var users = await store.OpenUsersAsync(cancellationToken);
        var favorites = await users.QueryAsync<(Guid DeckId, string? Name, DateTime CreatedAt)>(new CommandDefinition(
            """
            select f.deck_id, p.name, f.created_at
            from users.deck_favorites f left join users.public_decks p on p.deck_id = f.deck_id
            where f.account_id = @ownerId
            order by f.created_at
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("favorites.json", favorites.Select(row => new { row.DeckId, row.Name, row.CreatedAt }), cancellationToken);
        var following = await users.QueryAsync<(Guid AuthorId, DateTime CreatedAt)>(new CommandDefinition(
            "select author_id, created_at from users.author_follows where follower_id = @ownerId order by created_at",
            new { ownerId },
            cancellationToken: cancellationToken));
        await archive.WriteJsonAsync("following.json", following.Select(row => new { row.AuthorId, row.CreatedAt }), cancellationToken);
    }

    public async Task DeleteAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> deckIds;
        await using (var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            deckIds = [.. await connection.QueryAsync<Guid>(new CommandDefinition(
                "select id from content.decks where owner_id = @ownerId", new { ownerId }, transaction, cancellationToken: cancellationToken))];

            // The foreign keys delete the lines, the line tags and the local tags of the decks.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                delete from content.decks where owner_id = @ownerId;
                delete from content.tags where owner_id = @ownerId;
                delete from content.deck_folders where owner_id = @ownerId;
                delete from content.category_rules where owner_id = @ownerId;
                """,
                new { ownerId },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await using var users = await store.OpenUsersAsync(cancellationToken);

        // The copies of other users keep their cards, but they lose the link to the deleted decks.
        var copies = await users.QueryAsync<(Guid CopyDeckId, Guid CopyOwnerId)>(new CommandDefinition(
            """
            select c.copy_deck_id, d.owner_id
            from users.public_deck_copies c join users.deck_directory d on d.deck_id = c.copy_deck_id
            where c.source_deck_id = any(@deckIds)
            """,
            new { deckIds = deckIds.ToArray() },
            cancellationToken: cancellationToken));
        foreach (var group in copies.GroupBy(copy => copy.CopyOwnerId))
        {
            await using var shard = await store.OpenAsync(group.Key, ContentAccess.Write, cancellationToken);
            await shard.ExecuteAsync(new CommandDefinition(
                "update content.decks set source_deck_id = null where owner_id = @ownerId and id = any(@copyIds)",
                new { ownerId = group.Key, copyIds = group.Select(copy => copy.CopyDeckId).ToArray() },
                cancellationToken: cancellationToken));
        }

        await users.ExecuteAsync(new CommandDefinition(
            """
            delete from users.public_deck_copies where copy_deck_id = any(@deckIds) or source_deck_id = any(@deckIds);
            delete from users.deck_favorites where account_id = @ownerId or deck_id = any(@deckIds);
            delete from users.author_follows where follower_id = @ownerId or author_id = @ownerId;
            delete from users.public_decks where owner_id = @ownerId;
            delete from users.deck_directory where owner_id = @ownerId;
            """,
            new { ownerId, deckIds = deckIds.ToArray() },
            cancellationToken: cancellationToken));
    }
}
