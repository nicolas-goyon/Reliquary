using Dapper;
using Npgsql;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Collection.Data;
using static Reliquary.Collection.CollectionRules;

namespace Reliquary.Collection;

/// <summary>
/// The advanced fields that an owner enabled. See docs/architecture/collection.md.
/// </summary>
/// <remarks>
/// The API ignores a disabled field in a request and never returns it.
/// The stored values of a field stay when the owner disables the field, and come back when the owner enables it again.
/// </remarks>
/// <param name="Enabled">The names of the enabled fields. See <see cref="CollectionRules.Fields"/>.</param>
internal sealed record AdvancedFields(IReadOnlySet<string> Enabled)
{
    /// <summary>Gets the settings of an owner with no enabled field.</summary>
    public static AdvancedFields None { get; } = new(new HashSet<string>());

    public bool Finish => Enabled.Contains(Fields.Finish);

    public bool Condition => Enabled.Contains(Fields.Condition);

    public bool PurchasePrice => Enabled.Contains(Fields.PurchasePrice);

    public bool AcquiredOn => Enabled.Contains(Fields.AcquiredOn);

    public bool Note => Enabled.Contains(Fields.Note);

    /// <summary>
    /// Removes the values of the disabled fields from the card fields of a request.
    /// </summary>
    public CardFields Input(CardFields fields) => fields with
    {
        Finish = Finish ? fields.Finish : null,
        Condition = Condition ? fields.Condition : null,
        PurchasePrice = PurchasePrice ? fields.PurchasePrice : null,
        PurchaseCurrency = PurchasePrice ? fields.PurchaseCurrency : null,
        AcquiredOn = AcquiredOn ? fields.AcquiredOn : null,
        Note = Note ? fields.Note : null,
    };

    /// <summary>
    /// Removes the disabled fields from a card of a request. The API ignores these fields.
    /// </summary>
    public Api.CardRequest Input(Api.CardRequest card) => card with
    {
        Finish = Finish ? card.Finish : null,
        Condition = Condition ? card.Condition : null,
        PurchasePrice = PurchasePrice ? card.PurchasePrice : null,
        PurchaseCurrency = PurchasePrice ? card.PurchaseCurrency : null,
        AcquiredOn = AcquiredOn ? card.AcquiredOn : null,
        Note = Note ? card.Note : null,
    };

    /// <summary>
    /// Removes the changes of the disabled fields from the changes of an entry. The stored values of these fields stay.
    /// </summary>
    public EntryChanges Input(EntryChanges changes) => changes with
    {
        Finish = Finish ? changes.Finish : null,
        Condition = Condition ? changes.Condition : null,
        PurchasePrice = PurchasePrice ? changes.PurchasePrice : null,
        PurchaseCurrency = PurchasePrice ? changes.PurchaseCurrency : null,
        ClearPurchasePrice = PurchasePrice && changes.ClearPurchasePrice,
        AcquiredOn = AcquiredOn ? changes.AcquiredOn : null,
        ClearAcquiredOn = AcquiredOn && changes.ClearAcquiredOn,
        Note = Note ? changes.Note : null,
    };

    /// <summary>
    /// Hides the values of the disabled fields in a response.
    /// </summary>
    public CardFields Output(CardFields fields) => Input(fields);
}

/// <summary>
/// Reads and writes the collection settings of an owner.
/// </summary>
internal sealed class SettingsService(CollectionStore store, TimeProvider time)
{
    public async Task<AdvancedFields> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await GetAsync(connection, ownerId, cancellationToken);
    }

    public static async Task<AdvancedFields> GetAsync(NpgsqlConnection connection, Guid ownerId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        var fields = await connection.QuerySingleOrDefaultAsync<string[]>(new CommandDefinition(
            "select advanced_fields from content.collection_settings where owner_id = @ownerId",
            new { ownerId },
            transaction,
            cancellationToken: cancellationToken));
        return fields is null ? AdvancedFields.None : new AdvancedFields(fields.ToHashSet(StringComparer.Ordinal));
    }

    public async Task<AdvancedFields> SetAsync(Guid ownerId, IReadOnlyCollection<string> fields, CancellationToken cancellationToken)
    {
        var ordered = CollectionRules.AdvancedFields.Where(fields.Contains).ToArray();
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into content.collection_settings (owner_id, advanced_fields, updated_at) values (@ownerId, @fields, @now)
            on conflict (owner_id) do update set advanced_fields = excluded.advanced_fields, updated_at = excluded.updated_at
            """,
            new { ownerId, fields = ordered, now = time.GetUtcNow().UtcDateTime },
            cancellationToken: cancellationToken));
        return new AdvancedFields(ordered.ToHashSet(StringComparer.Ordinal));
    }
}
