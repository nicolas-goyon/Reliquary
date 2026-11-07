using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Cards.Import.Scryfall;

namespace Reliquary.Cards.Import;

/// <summary>
/// Imports the sets, the tags, the rulings and the MTGJSON identifiers.
/// Each method replaces the data of its kind in one transaction.
/// </summary>
/// <param name="database">The databases.</param>
/// <param name="time">The clock.</param>
internal sealed class ReferenceImporters(IDatabase database, TimeProvider time)
{
    private const int CommandTimeoutSeconds = 1800;

    private static readonly CopyTable<ScryfallTag> TagTable = new("reference.tags",
    [
        new("id", NpgsqlDbType.Uuid, tag => tag.Id),
        new("kind", NpgsqlDbType.Text, tag => tag.Type == "illustration" ? "illustration" : "oracle"),
        new("slug", NpgsqlDbType.Text, tag => tag.Slug),
        new("label", NpgsqlDbType.Text, tag => tag.Label),
        new("description", NpgsqlDbType.Text, tag => tag.Description),
        new("parent_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, tag => (tag.ParentIds ?? []).ToArray()),
        new("aliases", NpgsqlDbType.Array | NpgsqlDbType.Text, tag => (tag.Aliases ?? []).ToArray()),
    ]);

    private static readonly CopyTable<(Guid TagId, Guid TargetId, string Weight)> OracleTaggingTable = new("reference.oracle_taggings",
    [
        new("tag_id", NpgsqlDbType.Uuid, item => item.TagId),
        new("oracle_id", NpgsqlDbType.Uuid, item => item.TargetId),
        new("weight", NpgsqlDbType.Text, item => item.Weight),
    ]);

    private static readonly CopyTable<(Guid TagId, Guid TargetId, string Weight)> IllustrationTaggingTable = new("reference.illustration_taggings",
    [
        new("tag_id", NpgsqlDbType.Uuid, item => item.TagId),
        new("illustration_id", NpgsqlDbType.Uuid, item => item.TargetId),
        new("weight", NpgsqlDbType.Text, item => item.Weight),
    ]);

    private static readonly CopyTable<ScryfallRuling> RulingTable = new("reference.rulings",
    [
        new("oracle_id", NpgsqlDbType.Uuid, ruling => ruling.OracleId),
        new("source", NpgsqlDbType.Text, ruling => ruling.Source),
        new("published_at", NpgsqlDbType.Date, ruling => ruling.PublishedAt),
        new("comment", NpgsqlDbType.Text, ruling => ruling.Comment),
    ]);

    /// <summary>
    /// Imports the sets of the Scryfall sets API.
    /// </summary>
    /// <param name="content">The JSON list of the sets.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The number of sets.</returns>
    public async Task<int> ImportSetsAsync(Stream content, CancellationToken cancellationToken)
    {
        var list = await JsonSerializer.DeserializeAsync(content, ScryfallJson.Default.ScryfallListScryfallSet, cancellationToken)
            ?? throw new JsonException("The sets file contains no list.");
        var now = time.GetUtcNow();

        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reference.sets (code, scryfall_id, name, set_type, released_at, block_code, block, parent_set_code, card_count, digital, icon_svg_uri, updated_at)
            values (@Code, @Id, @Name, @SetType, @ReleasedAt, @BlockCode, @Block, @ParentSetCode, @CardCount, @Digital, @IconSvgUri, @now)
            on conflict (code) do update set
                scryfall_id = excluded.scryfall_id, name = excluded.name, set_type = excluded.set_type,
                released_at = excluded.released_at, block_code = excluded.block_code, block = excluded.block,
                parent_set_code = excluded.parent_set_code, card_count = excluded.card_count, digital = excluded.digital,
                icon_svg_uri = excluded.icon_svg_uri, updated_at = excluded.updated_at
            """,
            list.Data.Select(set => new
            {
                set.Code,
                set.Id,
                set.Name,
                set.SetType,
                ReleasedAt = set.ReleasedAt?.ToDateTime(TimeOnly.MinValue),
                set.BlockCode,
                set.Block,
                set.ParentSetCode,
                set.CardCount,
                set.Digital,
                set.IconSvgUri,
                now,
            }),
            transaction,
            CommandTimeoutSeconds,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return list.Data.Count;
    }

    /// <summary>
    /// Imports a JSONL file of Oracle tags or illustration tags, with their links, then computes the descendants of each tag.
    /// </summary>
    /// <param name="kind">The kind of the tags: <c>oracle</c> or <c>illustration</c>.</param>
    /// <param name="content">The JSONL file.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The number of tags.</returns>
    public async Task<int> ImportTagsAsync(string kind, Stream content, CancellationToken cancellationToken)
    {
        var tags = new List<ScryfallTag>();
        var taggings = new Dictionary<(Guid TagId, Guid TargetId), string>();
        using (var reader = new StreamReader(content))
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var tag = ScryfallJson.Read(line, ScryfallJson.Default.ScryfallTag);
                tags.Add(tag);
                foreach (var tagging in tag.Taggings ?? [])
                {
                    if ((kind == "oracle" ? tagging.OracleId : tagging.IllustrationId) is { } target)
                    {
                        taggings.TryAdd((tag.Id, target), tagging.Weight ?? "median");
                    }
                }

                tag.Taggings = null;
            }
        }

        var taggingTable = kind == "oracle" ? OracleTaggingTable : IllustrationTaggingTable;
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            delete from reference.tag_closure where ancestor_id in (select id from reference.tags where kind = @kind);
            delete from {taggingTable.Table};
            delete from reference.tags where kind = @kind;
            """,
            new { kind },
            transaction,
            CommandTimeoutSeconds,
            cancellationToken: cancellationToken));

        await TagTable.WriteAllAsync(connection, tags.DistinctBy(tag => tag.Id).DistinctBy(tag => tag.Slug), cancellationToken);
        await taggingTable.WriteAllAsync(connection, taggings.Select(item => (item.Key.TagId, item.Key.TargetId, item.Value)), cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reference.tag_closure (ancestor_id, descendant_id)
            with recursive closure (ancestor_id, descendant_id) as (
                select id, id from reference.tags where kind = @kind
                union
                select parent.id, closure.descendant_id
                from closure
                join reference.tags child on child.id = closure.ancestor_id
                join reference.tags parent on parent.id = any (child.parent_ids)
            )
            select ancestor_id, descendant_id from closure
            """,
            new { kind },
            transaction,
            CommandTimeoutSeconds,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return tags.Count;
    }

    /// <summary>
    /// Imports the JSONL file of the rulings.
    /// </summary>
    /// <param name="content">The JSONL file.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The number of rulings.</returns>
    public async Task<int> ImportRulingsAsync(Stream content, CancellationToken cancellationToken)
    {
        var rulings = new List<ScryfallRuling>();
        using (var reader = new StreamReader(content))
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    rulings.Add(ScryfallJson.Read(line, ScryfallJson.Default.ScryfallRuling));
                }
            }
        }

        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("delete from reference.rulings", transaction: transaction, cancellationToken: cancellationToken));
        await RulingTable.WriteAllAsync(connection, rulings, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rulings.Count;
    }

    /// <summary>
    /// Imports the CSV file of the card identifiers of MTGJSON into the column <c>external_ids</c> of the printings.
    /// </summary>
    /// <param name="content">The CSV file.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The number of printings that the import changed.</returns>
    public async Task<int> ImportIdentifiersAsync(Stream content, CancellationToken cancellationToken)
    {
        var identifiers = new Dictionary<Guid, string>();
        using (var reader = new StreamReader(content, Encoding.UTF8))
        {
            var header = (await reader.ReadLineAsync(cancellationToken))?.Split(',')
                ?? throw new InvalidDataException("The identifiers file is empty.");
            var scryfallColumn = Array.IndexOf(header, "scryfallId");
            var uuidColumn = Array.IndexOf(header, "uuid");
            if (scryfallColumn < 0 || uuidColumn < 0)
            {
                throw new InvalidDataException("The identifiers file has no column \"scryfallId\" or \"uuid\".");
            }

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var values = line.Split(',');
                if (values.Length != header.Length || !Guid.TryParse(values[scryfallColumn], out var scryfallId) || identifiers.ContainsKey(scryfallId))
                {
                    continue;
                }

                var ids = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["mtgjsonUuid"] = values[uuidColumn] };
                for (var index = 0; index < header.Length; index++)
                {
                    if (index != scryfallColumn && index != uuidColumn && values[index].Length > 0 && !header[index].StartsWith("scryfall", StringComparison.Ordinal))
                    {
                        ids[header[index]] = values[index];
                    }
                }

                identifiers[scryfallId] = JsonSerializer.Serialize(ids);
            }
        }

        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "create temp table import_identifiers (printing_id uuid primary key, ids jsonb not null) on commit drop",
            transaction: transaction,
            cancellationToken: cancellationToken));

        var table = new CopyTable<KeyValuePair<Guid, string>>("import_identifiers",
        [
            new("printing_id", NpgsqlDbType.Uuid, item => item.Key),
            new("ids", NpgsqlDbType.Jsonb, item => item.Value),
        ]);
        await table.WriteAllAsync(connection, identifiers, cancellationToken);

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            update reference.printings p set external_ids = i.ids
            from import_identifiers i
            where p.id = i.printing_id and p.external_ids is distinct from i.ids
            """,
            transaction: transaction,
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }
}
