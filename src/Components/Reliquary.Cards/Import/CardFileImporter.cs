using Dapper;
using Npgsql;
using NpgsqlTypes;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Cards.Import.Scryfall;

namespace Reliquary.Cards.Import;

/// <summary>
/// The result of the import of a card file.
/// </summary>
/// <param name="CardCount">The number of Oracle cards in the file.</param>
/// <param name="PrintingCount">The number of printings in the file.</param>
/// <param name="ChangedCards">The number of Oracle cards that the import added or changed.</param>
/// <param name="ChangedPrintings">The number of printings that the import added or changed.</param>
/// <param name="DeletedPrintings">The number of printings that the import deleted, because the file does not contain them.</param>
/// <param name="SkippedLines">The number of card objects without an Oracle identifier.</param>
internal sealed record CardImportResult(int CardCount, int PrintingCount, int ChangedCards, int ChangedPrintings, int DeletedPrintings, int SkippedLines);

/// <summary>
/// The card data of the reference schema changed. Consumers can, for example, check the legality of the decks again.
/// </summary>
/// <param name="Source">The name of the source file, for example <c>scryfall:all_cards</c>.</param>
/// <param name="ChangedCards">The number of Oracle cards that the import added or changed.</param>
/// <param name="ChangedPrintings">The number of printings that the import added or changed.</param>
/// <param name="DeletedPrintings">The number of printings that the import deleted.</param>
[DomainEvent("cards.card_data.imported", 1)]
public sealed record CardDataImported(string Source, int ChangedCards, int ChangedPrintings, int DeletedPrintings) : IDomainEvent;

/// <summary>
/// Imports a JSONL file of Scryfall card objects into the reference tables.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The importer reads the file one line at a time and writes the printings to staging tables with COPY.</item>
/// <item>It keeps the Oracle cards, the names and the relations in memory, then writes them to the staging tables.</item>
/// <item>In one transaction, it changes only the rows whose content hash changed, and it deletes the printings that the file does not contain.</item>
/// </list>
/// Two imports of the same file give the same data.
/// </remarks>
/// <param name="database">The databases.</param>
/// <param name="events">The domain event writer.</param>
/// <param name="time">The clock.</param>
internal sealed class CardFileImporter(IDatabase database, IDomainEventWriter events, TimeProvider time)
{
    /// <summary>The importer deletes the missing printings only when the file contains at least this part of the current printings.</summary>
    private const double MinimumShareForDeletion = 0.9;

    private const int CommandTimeoutSeconds = 3600;

    /// <summary>
    /// The order of the printings of a card for the choice of its default printing: the first printing is the default printing.
    /// </summary>
    private const string BestPrintingOrder = """
        (p.lang = 'en') desc,
        (p.released_at <= current_date) desc,
        ('paper' = any (p.games)) desc,
        (p.set_type in ('expansion', 'core', 'masters', 'draft_innovation', 'commander', 'starter', 'duel_deck', 'eternal', 'planechase', 'archenemy')) desc,
        (p.collector_number ~ '^[0-9]+[a-z]?$') desc,
        (p.frame in ('2015', '2003')) desc,
        (p.promo or p.variation or p.oversized or p.full_art) asc,
        (p.border_color = 'black') desc,
        (p.frame_effects && array['showcase', 'extendedart', 'etched', 'inverted', 'borderless']) asc,
        (p.image_status = 'highres_scan') desc,
        p.released_at desc,
        p.collector_number_value nulls last,
        p.id
        """;

    private static readonly CopyTable<(OracleRow Row, byte[] Hash)> OracleTable = new("reference.import_oracle_cards",
    [
        new("id", NpgsqlDbType.Uuid, item => item.Row.Id),
        new("name", NpgsqlDbType.Text, item => item.Row.Name),
        new("name_normalized", NpgsqlDbType.Text, item => item.Row.NameNormalized),
        new("layout", NpgsqlDbType.Text, item => item.Row.Layout),
        new("mana_cost", NpgsqlDbType.Text, item => item.Row.ManaCost),
        new("mana_value", NpgsqlDbType.Numeric, item => item.Row.ManaValue),
        new("type_line", NpgsqlDbType.Text, item => item.Row.TypeLine),
        new("oracle_text", NpgsqlDbType.Text, item => item.Row.OracleText),
        new("colors", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.Colors),
        new("colors_mask", NpgsqlDbType.Smallint, item => item.Row.ColorsMask),
        new("color_identity", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.ColorIdentity),
        new("color_identity_mask", NpgsqlDbType.Smallint, item => item.Row.ColorIdentityMask),
        new("color_indicator", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.ColorIndicator),
        new("produced_mana", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.ProducedMana),
        new("produced_mana_mask", NpgsqlDbType.Smallint, item => item.Row.ProducedManaMask),
        new("keywords", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.Keywords),
        new("power", NpgsqlDbType.Text, item => item.Row.Power),
        new("toughness", NpgsqlDbType.Text, item => item.Row.Toughness),
        new("loyalty", NpgsqlDbType.Text, item => item.Row.Loyalty),
        new("defense", NpgsqlDbType.Text, item => item.Row.Defense),
        new("hand_modifier", NpgsqlDbType.Text, item => item.Row.HandModifier),
        new("life_modifier", NpgsqlDbType.Text, item => item.Row.LifeModifier),
        new("legalities", NpgsqlDbType.Jsonb, item => item.Row.Legalities),
        new("reserved", NpgsqlDbType.Boolean, item => item.Row.Reserved),
        new("game_changer", NpgsqlDbType.Boolean, item => item.Row.GameChanger),
        new("edhrec_rank", NpgsqlDbType.Integer, item => item.Row.EdhrecRank),
        new("penny_rank", NpgsqlDbType.Integer, item => item.Row.PennyRank),
        new("content_hash", NpgsqlDbType.Bytea, item => item.Hash),
    ]);

    private static readonly CopyTable<(Guid OracleId, FaceRow Face)> FaceTable = new("reference.import_card_faces",
    [
        new("oracle_id", NpgsqlDbType.Uuid, item => item.OracleId),
        new("face_index", NpgsqlDbType.Smallint, item => item.Face.Index),
        new("name", NpgsqlDbType.Text, item => item.Face.Name),
        new("mana_cost", NpgsqlDbType.Text, item => item.Face.ManaCost),
        new("mana_value", NpgsqlDbType.Numeric, item => item.Face.ManaValue),
        new("type_line", NpgsqlDbType.Text, item => item.Face.TypeLine),
        new("oracle_text", NpgsqlDbType.Text, item => item.Face.OracleText),
        new("colors", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Face.Colors),
        new("colors_mask", NpgsqlDbType.Smallint, item => item.Face.ColorsMask),
        new("color_indicator", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Face.ColorIndicator),
        new("power", NpgsqlDbType.Text, item => item.Face.Power),
        new("toughness", NpgsqlDbType.Text, item => item.Face.Toughness),
        new("loyalty", NpgsqlDbType.Text, item => item.Face.Loyalty),
        new("defense", NpgsqlDbType.Text, item => item.Face.Defense),
    ]);

    private static readonly CopyTable<NameRow> NameTable = new("reference.import_card_names",
    [
        new("oracle_id", NpgsqlDbType.Uuid, row => row.OracleId),
        new("name", NpgsqlDbType.Text, row => row.Name),
        new("name_normalized", NpgsqlDbType.Text, row => row.NameNormalized),
        new("lang", NpgsqlDbType.Text, row => row.Lang),
        new("kind", NpgsqlDbType.Text, row => row.Kind),
    ]);

    private static readonly CopyTable<RelationRow> RelationTable = new("reference.import_card_relations",
    [
        new("oracle_id", NpgsqlDbType.Uuid, row => row.OracleId),
        new("related_printing_id", NpgsqlDbType.Uuid, row => row.RelatedPrintingId),
        new("related_name", NpgsqlDbType.Text, row => row.RelatedName),
        new("component", NpgsqlDbType.Text, row => row.Component),
    ]);

    private static readonly CopyTable<(PrintingRow Row, byte[] Hash)> PrintingTable = new("reference.import_printings",
    [
        new("id", NpgsqlDbType.Uuid, item => item.Row.Id),
        new("oracle_id", NpgsqlDbType.Uuid, item => item.Row.OracleId),
        new("set_code", NpgsqlDbType.Text, item => item.Row.SetCode),
        new("set_name", NpgsqlDbType.Text, item => item.Row.SetName),
        new("set_type", NpgsqlDbType.Text, item => item.Row.SetType),
        new("collector_number", NpgsqlDbType.Text, item => item.Row.CollectorNumber),
        new("collector_number_value", NpgsqlDbType.Integer, item => item.Row.CollectorNumberValue),
        new("lang", NpgsqlDbType.Text, item => item.Row.Lang),
        new("released_at", NpgsqlDbType.Date, item => item.Row.ReleasedAt),
        new("rarity", NpgsqlDbType.Text, item => item.Row.Rarity),
        new("rarity_rank", NpgsqlDbType.Smallint, item => item.Row.RarityRank),
        new("finishes", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.Finishes),
        new("games", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.Games),
        new("digital", NpgsqlDbType.Boolean, item => item.Row.Digital),
        new("promo", NpgsqlDbType.Boolean, item => item.Row.Promo),
        new("promo_types", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.PromoTypes),
        new("reprint", NpgsqlDbType.Boolean, item => item.Row.Reprint),
        new("variation", NpgsqlDbType.Boolean, item => item.Row.Variation),
        new("oversized", NpgsqlDbType.Boolean, item => item.Row.Oversized),
        new("full_art", NpgsqlDbType.Boolean, item => item.Row.FullArt),
        new("textless", NpgsqlDbType.Boolean, item => item.Row.Textless),
        new("booster", NpgsqlDbType.Boolean, item => item.Row.Booster),
        new("story_spotlight", NpgsqlDbType.Boolean, item => item.Row.StorySpotlight),
        new("content_warning", NpgsqlDbType.Boolean, item => item.Row.ContentWarning),
        new("frame", NpgsqlDbType.Text, item => item.Row.Frame),
        new("frame_effects", NpgsqlDbType.Array | NpgsqlDbType.Text, item => item.Row.FrameEffects),
        new("border_color", NpgsqlDbType.Text, item => item.Row.BorderColor),
        new("security_stamp", NpgsqlDbType.Text, item => item.Row.SecurityStamp),
        new("watermark", NpgsqlDbType.Text, item => item.Row.Watermark),
        new("artist", NpgsqlDbType.Text, item => item.Row.Artist),
        new("illustration_id", NpgsqlDbType.Uuid, item => item.Row.IllustrationId),
        new("flavor_text", NpgsqlDbType.Text, item => item.Row.FlavorText),
        new("flavor_name", NpgsqlDbType.Text, item => item.Row.FlavorName),
        new("printed_name", NpgsqlDbType.Text, item => item.Row.PrintedName),
        new("printed_type_line", NpgsqlDbType.Text, item => item.Row.PrintedTypeLine),
        new("printed_text", NpgsqlDbType.Text, item => item.Row.PrintedText),
        new("image_status", NpgsqlDbType.Text, item => item.Row.ImageStatus),
        new("image_uris", NpgsqlDbType.Jsonb, item => item.Row.ImageUris),
        new("prices", NpgsqlDbType.Jsonb, item => item.Row.Prices),
        new("multiverse_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer, item => item.Row.MultiverseIds),
        new("mtgo_id", NpgsqlDbType.Integer, item => item.Row.MtgoId),
        new("arena_id", NpgsqlDbType.Integer, item => item.Row.ArenaId),
        new("tcgplayer_id", NpgsqlDbType.Integer, item => item.Row.TcgplayerId),
        new("cardmarket_id", NpgsqlDbType.Integer, item => item.Row.CardmarketId),
        new("content_hash", NpgsqlDbType.Bytea, item => item.Hash),
    ]);

    private static readonly CopyTable<(Guid PrintingId, PrintingFaceRow Face)> PrintingFaceTable = new("reference.import_printing_faces",
    [
        new("printing_id", NpgsqlDbType.Uuid, item => item.PrintingId),
        new("face_index", NpgsqlDbType.Smallint, item => item.Face.Index),
        new("name", NpgsqlDbType.Text, item => item.Face.Name),
        new("printed_name", NpgsqlDbType.Text, item => item.Face.PrintedName),
        new("printed_type_line", NpgsqlDbType.Text, item => item.Face.PrintedTypeLine),
        new("printed_text", NpgsqlDbType.Text, item => item.Face.PrintedText),
        new("flavor_text", NpgsqlDbType.Text, item => item.Face.FlavorText),
        new("artist", NpgsqlDbType.Text, item => item.Face.Artist),
        new("illustration_id", NpgsqlDbType.Uuid, item => item.Face.IllustrationId),
        new("watermark", NpgsqlDbType.Text, item => item.Face.Watermark),
        new("image_uris", NpgsqlDbType.Jsonb, item => item.Face.ImageUris),
    ]);

    /// <summary>
    /// Imports a card file.
    /// </summary>
    /// <param name="source">The name of the source, for the domain event.</param>
    /// <param name="content">The content of the file: one card object on each line.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The result of the import.</returns>
    public async Task<CardImportResult> ImportAsync(string source, Stream content, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        await ExecuteAsync(connection, null, """
            truncate reference.import_oracle_cards, reference.import_card_faces, reference.import_card_names,
                     reference.import_card_relations, reference.import_printings, reference.import_printing_faces
            """, cancellationToken);

        var staged = await StageAsync(content, connection, cancellationToken);

        // Fresh statistics give the planner hash joins on the staging tables, not nested loops.
        await ExecuteAsync(connection, null, """
            analyze reference.import_oracle_cards, reference.import_card_faces, reference.import_card_names,
                    reference.import_card_relations, reference.import_printings, reference.import_printing_faces
            """, cancellationToken);

        return await MergeAsync(source, connection, staged, cancellationToken);
    }

    private async Task<StagedFile> StageAsync(Stream content, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var oracles = new Dictionary<Guid, OracleCandidate>();
        var names = new HashSet<NameRow>();
        var relations = new Dictionary<(Guid OracleId, string Component, string Name), RelationRow>();
        var printingCount = 0;
        var skipped = 0;

        // The printings and their faces go to the database during the read, on two other connections.
        await using var printingConnection = await database.OpenReferenceAsync(cancellationToken);
        await using var faceConnection = await database.OpenReferenceAsync(cancellationToken);
        await using (var printings = await PrintingTable.BeginAsync(printingConnection, cancellationToken))
        await using (var printingFaces = await PrintingFaceTable.BeginAsync(faceConnection, cancellationToken))
        {
            using var reader = new StreamReader(content);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                line = line.Trim().TrimEnd(',');
                if (line.Length == 0 || line is "[" or "]")
                {
                    continue;
                }

                var card = ScryfallJson.Read(line, ScryfallJson.Default.ScryfallCard);
                if (CardMapper.OracleIdOf(card) is not { } oracleId)
                {
                    skipped++;
                    continue;
                }

                var oracle = CardMapper.ToOracle(card, oracleId);
                var candidate = new OracleCandidate(oracle, card.Lang == "en", card.ReleasedAt, card.Id);
                if (!oracles.TryGetValue(oracleId, out var current) || candidate.IsBetterThan(current))
                {
                    oracles[oracleId] = candidate;
                }

                names.UnionWith(CardMapper.Names(card, oracle));

                foreach (var relation in CardMapper.Relations(card, oracleId))
                {
                    var key = (relation.OracleId, relation.Component, relation.RelatedName);
                    if (!relations.TryGetValue(key, out var existing) || relation.RelatedPrintingId.CompareTo(existing.RelatedPrintingId) > 0)
                    {
                        relations[key] = relation;
                    }
                }

                var printing = CardMapper.ToPrinting(card, oracleId);
                await PrintingTable.WriteAsync(printings, (printing, CardMapper.Hash(printing)), cancellationToken);
                foreach (var face in printing.Faces)
                {
                    await PrintingFaceTable.WriteAsync(printingFaces, (printing.Id, face), cancellationToken);
                }

                printingCount++;
            }

            await printings.CompleteAsync(cancellationToken);
            await printingFaces.CompleteAsync(cancellationToken);
        }

        var relationsByOracle = relations.Values
            .GroupBy(relation => relation.OracleId)
            .ToDictionary(group => group.Key, group => group.OrderBy(relation => relation.Component, StringComparer.Ordinal).ThenBy(relation => relation.RelatedName, StringComparer.Ordinal).ToList());

        await OracleTable.WriteAllAsync(
            connection,
            oracles.Values.Select(candidate => (candidate.Row, CardMapper.Hash(new { candidate.Row, Relations = relationsByOracle.GetValueOrDefault(candidate.Row.Id) ?? [] }))),
            cancellationToken);
        await FaceTable.WriteAllAsync(connection, oracles.Values.SelectMany(candidate => candidate.Row.Faces.Select(face => (candidate.Row.Id, face))), cancellationToken);
        await NameTable.WriteAllAsync(connection, names, cancellationToken);
        await RelationTable.WriteAllAsync(connection, relations.Values, cancellationToken);

        return new StagedFile(oracles.Count, printingCount, skipped);
    }

    private async Task<CardImportResult> MergeAsync(string source, NpgsqlConnection connection, StagedFile staged, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow();

        await ExecuteAsync(connection, transaction, """
            create temp table changed_oracles on commit drop as
            select s.id from reference.import_oracle_cards s
            left join reference.oracle_cards o on o.id = s.id
            where o.id is null or o.content_hash <> s.content_hash;

            create temp table changed_printings on commit drop as
            select s.id from reference.import_printings s
            left join reference.printings p on p.id = s.id
            where p.id is null or p.content_hash <> s.content_hash;

            analyze changed_oracles, changed_printings;
            """, cancellationToken);

        var changedCards = await connection.ExecuteScalarAsync<int>(Command("select count(*) from changed_oracles", transaction, cancellationToken));
        var changedPrintings = await connection.ExecuteScalarAsync<int>(Command("select count(*) from changed_printings", transaction, cancellationToken));

        // The insert computes the columns that depend on the printings from the staging table of the printings.
        // A second update of each row would double the size of the table after a first import.
        await ExecuteAsync(connection, transaction, $"""
            with best as (
                select distinct on (p.oracle_id) p.oracle_id, p.id
                from reference.import_printings p
                where p.oracle_id in (select id from changed_oracles)
                order by p.oracle_id, {BestPrintingOrder}
            ),
            summary as (
                {PrintingSummary("reference.import_printings", "where p.oracle_id in (select id from changed_oracles)")}
            )
            insert into reference.oracle_cards ({OracleTable.ColumnList}, default_printing_id, first_released_at, is_extra, is_funny, search_hidden, updated_at)
            select {Prefixed(OracleTable.ColumnList, "o")}, b.id, m.released_at, {IsExtra}, {IsFunny}, {SearchHidden}, @now
            from reference.import_oracle_cards o
            left join best b on b.oracle_id = o.id
            left join summary m on m.oracle_id = o.id
            where o.id in (select id from changed_oracles)
            on conflict (id) do update set
                {UpdateSet(OracleTable.ColumnList, "id")},
                default_printing_id = excluded.default_printing_id,
                first_released_at = excluded.first_released_at,
                is_extra = excluded.is_extra,
                is_funny = excluded.is_funny,
                search_hidden = excluded.search_hidden,
                updated_at = excluded.updated_at;

            delete from reference.card_faces where oracle_id in (select id from changed_oracles);
            insert into reference.card_faces ({FaceTable.ColumnList})
            select {FaceTable.ColumnList} from reference.import_card_faces
            where oracle_id in (select id from changed_oracles);

            delete from reference.card_relations where oracle_id in (select id from changed_oracles);
            insert into reference.card_relations ({RelationTable.ColumnList})
            select {RelationTable.ColumnList} from reference.import_card_relations
            where oracle_id in (select id from changed_oracles)
            on conflict do nothing;

            delete from reference.card_names n
            where not exists (
                select 1 from reference.import_card_names s
                where s.oracle_id = n.oracle_id and s.name_normalized = n.name_normalized and s.lang = n.lang and s.kind = n.kind);
            insert into reference.card_names ({NameTable.ColumnList})
            select distinct on (oracle_id, name_normalized, lang, kind) {NameTable.ColumnList} from reference.import_card_names
            order by oracle_id, name_normalized, lang, kind, name
            on conflict do nothing;

            with english as (
                select distinct set_code, collector_number from reference.import_printings where lang = 'en'
            )
            insert into reference.printings ({PrintingTable.ColumnList}, default_lang, updated_at)
            select {Prefixed(PrintingTable.ColumnList, "s")}, (s.lang = 'en' or e.set_code is null), @now
            from reference.import_printings s
            left join english e on e.set_code = s.set_code and e.collector_number = s.collector_number
            where s.id in (select id from changed_printings)
            on conflict (id) do update set
                {UpdateSet(PrintingTable.ColumnList, "id")},
                default_lang = excluded.default_lang,
                updated_at = excluded.updated_at;

            delete from reference.printing_faces where printing_id in (select id from changed_printings);
            insert into reference.printing_faces ({PrintingFaceTable.ColumnList})
            select {PrintingFaceTable.ColumnList} from reference.import_printing_faces
            where printing_id in (select id from changed_printings);
            """, cancellationToken, new { now, extraLayouts = CardMapper.ExtraLayouts.ToArray() });

        var deletedPrintings = 0;
        var existing = await connection.ExecuteScalarAsync<long>(Command("select count(*) from reference.printings", transaction, cancellationToken));
        if (staged.PrintingCount >= existing * MinimumShareForDeletion)
        {
            deletedPrintings = await connection.ExecuteAsync(Command(
                "delete from reference.printings p where not exists (select 1 from reference.import_printings s where s.id = p.id)",
                transaction,
                cancellationToken));
        }

        await UpdateDerivedColumnsAsync(connection, transaction, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Reference, transaction, new CardDataImported(source, changedCards, changedPrintings, deletedPrintings), ownerId: null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Fresh statistics give good plans to the card search.
        await ExecuteAsync(connection, null, """
            analyze reference.oracle_cards, reference.card_faces, reference.card_names, reference.printings, reference.printing_faces
            """, cancellationToken);

        return new CardImportResult(staged.CardCount, staged.PrintingCount, changedCards, changedPrintings, deletedPrintings, staged.SkippedLines);
    }

    /// <summary>The condition of an extra card: a token, an emblem, an art card or a card with printings in token and memorabilia sets only.</summary>
    private const string IsExtra = "(o.layout = any (@extraLayouts) or not coalesce(m.game_card, false))";

    /// <summary>The condition of a funny card, as the keyword is:funny of Scryfall: a card that is not legal in Vintage, with a funny printing.</summary>
    private const string IsFunny = "(o.legalities->>'vintage' = 'not_legal' and coalesce(m.funny_printing, false))";

    /// <summary>
    /// The condition of a card that the search hides by default, as Scryfall does: a card with a content warning,
    /// or a card that is legal in no format, with a funny printing or with digital printings only,
    /// and without a printing in an Un-set or with an acorn stamp.
    /// Examples: the playtest cards, the Heroes of the Realm cards and the specialized variants of Alchemy.
    /// A card of a set that is not released yet is legal in no format, but the search shows it.
    /// </summary>
    private const string SearchHidden = """
        (coalesce(m.content_warning, false)
         or (not exists (select 1 from jsonb_each_text(o.legalities) legality where legality.value <> 'not_legal')
             and (coalesce(m.funny_printing, false) or not coalesce(m.paper, false))
             and not coalesce(m.un_printing, false)))
        """;

    /// <summary>
    /// Gives the summary of the printings of each card, alias <c>m</c> in <see cref="IsExtra"/>, <see cref="IsFunny"/> and <see cref="SearchHidden"/>.
    /// </summary>
    /// <remarks>
    /// A funny printing has a silver border, an acorn stamp or the promo type "playtest", or it is in a funny set.
    /// The sets of Unfinity are an exception: they contain cards that are legal in the Eternal formats, and their other cards are not funny.
    /// An "un" printing has an acorn stamp, or it is in one of the Un-sets: Unglued, Unhinged, Unstable, Unsanctioned and Unfinity.
    /// The search shows these cards by default. It hides the other funny cards, for example the holiday cards and the playtest cards.
    /// </remarks>
    private static string PrintingSummary(string table, string? filter) => $"""
        select p.oracle_id,
            min(p.released_at) as released_at,
            bool_or(p.set_type not in ('token', 'memorabilia')) as game_card,
            bool_or(p.border_color = 'silver' or p.security_stamp = 'acorn' or 'playtest' = any (p.promo_types)
                    or (p.set_type = 'funny' and p.set_code not in ('unf', 'sunf'))) as funny_printing,
            bool_or(p.security_stamp = 'acorn' or p.set_code in ('ugl', 'unh', 'ust', 'und', 'unf', 'sunf', 'una', 'ulst')) as un_printing,
            bool_or(p.content_warning) as content_warning,
            bool_or('paper' = any (p.games)) as paper
        from {table} p
        {filter}
        group by p.oracle_id
        """;

    /// <summary>
    /// Computes the columns that depend on other printings: the default printing, the first release date, the "extra" flag,
    /// the "funny" flag and the "hidden" flag of the Oracle cards, and the default language flag of the printings.
    /// </summary>
    /// <remarks>
    /// A printing is in the default language of the search when it is in English,
    /// or when its set has no English printing with the same collector number.
    /// </remarks>
    private static Task<int> UpdateDerivedColumnsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, transaction, $"""
            with english as (
                select distinct set_code, collector_number from reference.printings where lang = 'en'
            ),
            derived as (
                select p.id, (p.lang = 'en' or e.set_code is null) as default_lang
                from reference.printings p
                left join english e on e.set_code = p.set_code and e.collector_number = p.collector_number
            )
            update reference.printings p
            set default_lang = d.default_lang
            from derived d
            where p.id = d.id and p.default_lang is distinct from d.default_lang;

            with best as (
                select distinct on (p.oracle_id) p.oracle_id, p.id
                from reference.printings p
                order by p.oracle_id, {BestPrintingOrder}
            ),
            summary as (
                {PrintingSummary("reference.printings", null)}
            ),
            derived as (
                select o.id,
                    b.id as default_printing_id,
                    m.released_at as first_released_at,
                    {IsExtra} as is_extra,
                    {IsFunny} as is_funny,
                    {SearchHidden} as search_hidden
                from reference.oracle_cards o
                left join best b on b.oracle_id = o.id
                left join summary m on m.oracle_id = o.id
            )
            update reference.oracle_cards o
            set default_printing_id = d.default_printing_id,
                first_released_at = d.first_released_at,
                is_extra = d.is_extra,
                is_funny = d.is_funny,
                search_hidden = d.search_hidden
            from derived d
            where o.id = d.id
              and (o.default_printing_id is distinct from d.default_printing_id
                   or o.first_released_at is distinct from d.first_released_at
                   or o.is_extra is distinct from d.is_extra
                   or o.is_funny is distinct from d.is_funny
                   or o.search_hidden is distinct from d.search_hidden);
            """, cancellationToken, new { extraLayouts = CardMapper.ExtraLayouts.ToArray() });

    private static string Prefixed(string columnList, string alias) =>
        string.Join(", ", columnList.Split(", ").Select(column => $"{alias}.{column}"));

    private static string UpdateSet(string columnList, string key) =>
        string.Join(
            ",\n                ",
            columnList.Split(", ").Where(column => column != key).Select(column => $"{column} = excluded.{column}"));

    private static CommandDefinition Command(string sql, NpgsqlTransaction? transaction, CancellationToken cancellationToken, object? parameters = null) =>
        new(sql, parameters, transaction, CommandTimeoutSeconds, cancellationToken: cancellationToken);

    private static Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken, object? parameters = null) =>
        connection.ExecuteAsync(Command(sql, transaction, cancellationToken, parameters));

    private sealed record StagedFile(int CardCount, int PrintingCount, int SkippedLines);

    /// <summary>
    /// A card object that gives the Oracle data of a card. The import prefers an English printing, then the latest printing.
    /// </summary>
    private sealed record OracleCandidate(OracleRow Row, bool English, DateOnly ReleasedAt, Guid PrintingId)
    {
        public bool IsBetterThan(OracleCandidate other) =>
            English != other.English
                ? English
                : ReleasedAt != other.ReleasedAt
                    ? ReleasedAt > other.ReleasedAt
                    : PrintingId.CompareTo(other.PrintingId) > 0;
    }
}
