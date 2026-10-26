using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Cards.Api;

namespace Reliquary.Cards.Search;

/// <summary>
/// A request to the card search.
/// </summary>
/// <param name="Query">The query in the Scryfall syntax.</param>
/// <param name="Defaults">The display options of the query parameters.</param>
/// <param name="Page">The paging parameters.</param>
internal sealed record CardSearchRequest(string Query, SearchDefaults Defaults, PageRequest Page);

/// <summary>
/// The result of a card search: a page, or an error.
/// </summary>
internal abstract record CardSearchOutcome;

/// <summary>The search found a page of results.</summary>
/// <param name="Response">The page.</param>
internal sealed record CardSearchSucceeded(CardSearchResponse Response) : CardSearchOutcome;

/// <summary>The search failed because of the query.</summary>
/// <param name="Code">The stable error code.</param>
/// <param name="Message">The English message, in the strict mode of the writing rules.</param>
/// <param name="Position">The position of the error in the query, or <see langword="null"/>.</param>
/// <param name="Warnings">The warnings about the ignored terms.</param>
internal sealed record CardSearchFailed(string Code, string Message, int? Position, IReadOnlyList<SearchWarning> Warnings) : CardSearchOutcome;

/// <summary>The cursor is not valid for this query.</summary>
internal sealed record CardSearchInvalidCursor : CardSearchOutcome;

/// <summary>
/// The error codes of the card search.
/// </summary>
public static class CardSearchErrorCodes
{
    /// <summary>The query has a syntax error. The member <c>position</c> gives the index of the character.</summary>
    public const string SyntaxError = "search_syntax_error";

    /// <summary>The query has no search condition, for example because the search ignored all its terms.</summary>
    public const string EmptyQuery = "search_query_empty";

    /// <summary>The search took more time than the limit. The query is too complex, for example a slow regular expression.</summary>
    public const string Timeout = "search_timeout";
}

/// <summary>
/// Runs the card search: it parses the query, compiles it into one SQL query and reads one page of results.
/// </summary>
/// <param name="database">The databases.</param>
/// <param name="logger">The logger.</param>
internal sealed class CardSearch(IDatabase database, ILogger<CardSearch> logger)
{
    /// <summary>The maximum time of the SQL query of a search, in milliseconds.</summary>
    public const int StatementTimeoutMilliseconds = 5000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Searches the cards.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The page, or the error.</returns>
    public async Task<CardSearchOutcome> SearchAsync(CardSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parsed = SearchParser.Parse(request.Query);
        if (parsed.Error is { } syntax)
        {
            return new CardSearchFailed(CardSearchErrorCodes.SyntaxError, syntax.Message, syntax.Position, []);
        }

        var search = SearchCompiler.Compile(request.Query, parsed.Root, request.Defaults);
        if (search.IsEmpty)
        {
            return new CardSearchFailed(
                CardSearchErrorCodes.EmptyQuery,
                "The query has no search condition. Write a condition, for example a card name or \"t:creature\".",
                null,
                search.Warnings);
        }

        var key = CursorKey(request.Query, search);
        var offset = 0;
        if (request.Page.Cursor is { } cursor)
        {
            if (!Cursors.TryDecode<SearchCursor>(cursor, out var position) || position is null || position.Key != key || position.Offset < 0)
            {
                return new CardSearchInvalidCursor();
            }

            offset = position.Offset;
        }

        var parameters = new DynamicParameters();
        foreach (var (name, value) in search.Parameters)
        {
            parameters.Add(name, value);
        }

        parameters.Add("limit", request.Page.Limit + 1);
        parameters.Add("offset", offset);

        try
        {
            await using var connection = await database.OpenReferenceAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                $"set local statement_timeout = {StatementTimeoutMilliseconds}; set local jit = off",
                transaction: transaction,
                cancellationToken: cancellationToken));

            var rows = (await connection.QueryAsync<SearchRow>(new CommandDefinition(SearchSql.Page(search), parameters, transaction, cancellationToken: cancellationToken))).ToList();
            long total;
            if (rows.Count > 0)
            {
                total = rows[0].Total;
            }
            else
            {
                total = offset == 0 ? 0 : await connection.ExecuteScalarAsync<long>(new CommandDefinition(SearchSql.Count(search), parameters, transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);

            var hasMore = rows.Count > request.Page.Limit;
            var items = rows.Take(request.Page.Limit).Select(ToItem).ToList();
            var next = hasMore ? Cursors.Encode(new SearchCursor(offset + request.Page.Limit, key)) : null;
            return new CardSearchSucceeded(new CardSearchResponse(items, next, total, search.Warnings));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.QueryCanceled && !cancellationToken.IsCancellationRequested)
        {
            logger.SearchTimedOut(request.Query.Length, StatementTimeoutMilliseconds);
            return new CardSearchFailed(
                CardSearchErrorCodes.Timeout,
                $"The search took more than {StatementTimeoutMilliseconds / 1000} seconds and stopped. Make the query simpler, for example with fewer regular expressions.",
                null,
                search.Warnings);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InvalidRegularExpression)
        {
            var position = await FindInvalidRegexAsync(search, cancellationToken);
            return new CardSearchFailed(
                CardSearchErrorCodes.SyntaxError,
                $"The regular expression at position {position} is not valid. Correct the regular expression.",
                position,
                search.Warnings);
        }
    }

    /// <summary>
    /// Finds the term of the first regular expression that PostgreSQL cannot compile.
    /// </summary>
    private async Task<int> FindInvalidRegexAsync(CompiledSearch search, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenReferenceAsync(cancellationToken);
        foreach (var (parameter, position) in search.Regexes)
        {
            try
            {
                await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select '' ~* @pattern", new { pattern = search.Parameters[parameter] }, cancellationToken: cancellationToken));
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InvalidRegularExpression)
            {
                return position;
            }
        }

        return search.Regexes.Count > 0 ? search.Regexes[0].Position : 0;
    }

    /// <summary>
    /// Gives a key of the query and its display options. A cursor is valid only for the query that created it.
    /// </summary>
    private static string CursorKey(string query, CompiledSearch search)
    {
        var text = $"{query}\n{search.Unique}\n{search.Order}\n{search.Direction}\n{search.Prefer}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8);
    }

    private static CardSearchItem ToItem(SearchRow row)
    {
        var faces = JsonSerializer.Deserialize<List<FaceJson>>(row.Faces, Json) ?? [];
        var printingFaces = JsonSerializer.Deserialize<List<PrintingFaceJson>>(row.PrintingFaces, Json) ?? [];
        return new CardSearchItem(
            new CardSummaryResponse
            {
                Id = row.Id,
                Name = row.Name,
                Layout = row.Layout,
                ManaCost = row.ManaCost,
                ManaValue = row.ManaValue,
                TypeLine = row.TypeLine,
                OracleText = row.OracleText,
                Colors = row.Colors,
                ColorIdentity = row.ColorIdentity,
                Power = row.Power,
                Toughness = row.Toughness,
                Loyalty = row.Loyalty,
                Defense = row.Defense,
                Faces = [.. faces.Select(face => new CardFaceResponse(face.Name, face.ManaCost, face.ManaValue, face.TypeLine, face.OracleText, face.Colors ?? [], face.Power, face.Toughness, face.Loyalty, face.Defense))],
            },
            new PrintingSummaryResponse
            {
                Id = row.PrintingId,
                SetCode = row.SetCode,
                SetName = row.SetName,
                CollectorNumber = row.CollectorNumber,
                Lang = row.Lang,
                ReleasedAt = row.ReleasedAt,
                Rarity = row.Rarity,
                PrintedName = row.PrintedName,
                ImageUris = row.ImageUris is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(row.ImageUris, Json) ?? [],
                Faces = [.. printingFaces.Select(face => new PrintingFaceResponse(face.Name, face.PrintedName, face.ImageUris ?? []))],
            });
    }

    /// <summary>
    /// The position of the next page: the number of results before it, and the key of the query.
    /// </summary>
    /// <param name="Offset">The number of results before the page.</param>
    /// <param name="Key">The key of the query.</param>
    private sealed record SearchCursor(int Offset, string Key);

    private sealed class SearchRow
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Layout { get; set; } = string.Empty;

        public string? ManaCost { get; set; }

        public decimal ManaValue { get; set; }

        public string? TypeLine { get; set; }

        public string? OracleText { get; set; }

        public string[] Colors { get; set; } = [];

        public string[] ColorIdentity { get; set; } = [];

        public string? Power { get; set; }

        public string? Toughness { get; set; }

        public string? Loyalty { get; set; }

        public string? Defense { get; set; }

        public string Faces { get; set; } = "[]";

        public Guid PrintingId { get; set; }

        public string SetCode { get; set; } = string.Empty;

        public string SetName { get; set; } = string.Empty;

        public string CollectorNumber { get; set; } = string.Empty;

        public string Lang { get; set; } = string.Empty;

        public DateOnly ReleasedAt { get; set; }

        public string Rarity { get; set; } = string.Empty;

        public string? PrintedName { get; set; }

        public string? ImageUris { get; set; }

        public string PrintingFaces { get; set; } = "[]";

        public long Total { get; set; }
    }

    private sealed class FaceJson
    {
        public string Name { get; set; } = string.Empty;

        public string? ManaCost { get; set; }

        public decimal ManaValue { get; set; }

        public string? TypeLine { get; set; }

        public string? OracleText { get; set; }

        public List<string>? Colors { get; set; }

        public string? Power { get; set; }

        public string? Toughness { get; set; }

        public string? Loyalty { get; set; }

        public string? Defense { get; set; }
    }

    private sealed class PrintingFaceJson
    {
        public string Name { get; set; } = string.Empty;

        public string? PrintedName { get; set; }

        public Dictionary<string, string>? ImageUris { get; set; }
    }
}
