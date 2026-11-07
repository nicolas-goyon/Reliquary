using Dapper;
using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// A storage rule: a card search that suggests a location for a new card.
/// </summary>
internal sealed class StorageRuleRow
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public string Query { get; set; } = string.Empty;

    public int Priority { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string LocationName { get; set; } = string.Empty;

    public string? SectionName { get; set; }
}

/// <summary>
/// The operations on the storage rules. The rule with the lowest priority number applies first.
/// </summary>
internal sealed class StorageRuleService(CollectionStore store, ICardCatalog catalog, TimeProvider time)
{
    /// <summary>The maximum number of storage rules of an owner.</summary>
    public const int MaxRules = 100;

    /// <summary>The maximum length of the query of a rule.</summary>
    public const int MaxQueryLength = 500;

    private const string Columns = """
        r.id, r.location_id as LocationId, r.query, r.priority, r.created_at as CreatedAt, r.updated_at as UpdatedAt,
        coalesce(p.name, l.name) as LocationName, case when p.id is null then null else l.name end as SectionName
        """;

    private const string From = """
        from content.storage_rules r
        join content.locations l on l.id = r.location_id
        left join content.locations p on p.id = l.parent_id
        """;

    public async Task<IReadOnlyList<StorageRuleRow>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return [.. await connection.QueryAsync<StorageRuleRow>(new CommandDefinition(
            $"select {Columns} {From} where r.owner_id = @ownerId order by r.priority, r.created_at, r.id",
            new { ownerId },
            cancellationToken: cancellationToken))];
    }

    public async Task<StorageRuleRow> CreateAsync(Guid ownerId, Guid locationId, string query, int priority, CancellationToken cancellationToken)
    {
        await CheckQueryAsync(query, cancellationToken);
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await CheckLocationAsync(connection, transaction, ownerId, locationId, cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*)::int from content.storage_rules where owner_id = @ownerId", new { ownerId }, transaction, cancellationToken: cancellationToken));
        if (count >= MaxRules)
        {
            throw new CollectionException(StatusCodes.Status409Conflict, ErrorCodes.Conflict, $"You have {MaxRules} storage rules. Delete a rule, then try again.");
        }

        var id = Ids.New();
        var now = time.GetUtcNow().UtcDateTime;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.storage_rules (id, owner_id, location_id, query, priority, created_at, updated_at)
            values (@id, @ownerId, @locationId, @query, @priority, @now, @now)
            """,
            new { id, ownerId, locationId, query, priority, now },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(ownerId, id, cancellationToken);
    }

    public async Task<StorageRuleRow> UpdateAsync(Guid ownerId, Guid ruleId, Guid? locationId, string? query, int? priority, CancellationToken cancellationToken)
    {
        if (query is not null)
        {
            await CheckQueryAsync(query, cancellationToken);
        }

        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (locationId is { } newLocation)
        {
            await CheckLocationAsync(connection, transaction, ownerId, newLocation, cancellationToken);
        }

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            update content.storage_rules
            set location_id = coalesce(@locationId, location_id), query = coalesce(@query, query), priority = coalesce(@priority, priority), updated_at = @now
            where id = @ruleId and owner_id = @ownerId
            """,
            new { ruleId, ownerId, locationId, query, priority, now = time.GetUtcNow().UtcDateTime },
            transaction,
            cancellationToken: cancellationToken));
        if (changed == 0)
        {
            throw NotFound();
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(ownerId, ruleId, cancellationToken);
    }

    public async Task DeleteAsync(Guid ownerId, Guid ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "delete from content.storage_rules where id = @ruleId and owner_id = @ownerId", new { ruleId, ownerId }, cancellationToken: cancellationToken));
        if (deleted == 0)
        {
            throw NotFound();
        }
    }

    /// <summary>
    /// Finds the first rule, by priority, whose card search matches a card.
    /// </summary>
    /// <returns>The rule, or <see langword="null"/> when no rule matches.</returns>
    public async Task<StorageRuleRow?> SuggestAsync(Guid ownerId, Guid oracleId, CancellationToken cancellationToken)
    {
        foreach (var rule in await ListAsync(ownerId, cancellationToken))
        {
            try
            {
                if ((await catalog.MatchAsync(rule.Query, [oracleId], cancellationToken)).Contains(oracleId))
                {
                    return rule;
                }
            }
            catch (CardQueryException)
            {
                // A rule that the card search cannot use any more does not match. The user sees the error when they change the rule.
            }
        }

        return null;
    }

    private async Task<StorageRuleRow> GetAsync(Guid ownerId, Guid ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<StorageRuleRow>(new CommandDefinition(
            $"select {Columns} {From} where r.id = @ruleId and r.owner_id = @ownerId",
            new { ruleId, ownerId },
            cancellationToken: cancellationToken)) ?? throw NotFound();
    }

    private async Task CheckQueryAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await catalog.MatchAsync(query, [], cancellationToken);
        }
        catch (CardQueryException exception)
        {
            throw CollectionException.Validation(new FieldError("query", FieldErrorCodes.InvalidFormat, exception.Message));
        }
    }

    private static async Task CheckLocationAsync(Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction, Guid ownerId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken, transaction);
        if (location is null)
        {
            throw CollectionException.Validation(new FieldError("locationId", FieldErrorCodes.NotAllowed, "The location does not exist."));
        }

        if (location.DeckId is not null)
        {
            throw CollectionException.Validation(new FieldError("locationId", FieldErrorCodes.NotAllowed, "A storage rule cannot give the location of an assembled deck."));
        }
    }

    private static CollectionException NotFound() => CollectionException.NotFound("The storage rule does not exist.");
}
