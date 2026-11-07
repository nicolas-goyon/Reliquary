using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Exploration;

/// <summary>
/// Keeps the read model of the public decks up to date. The domain events of the decks start it.
/// </summary>
/// <remarks>
/// The indexer reads the current state of the deck in its shard, so an event that arrives two times,
/// or after a more recent event, gives the same result.
/// </remarks>
internal sealed class PublicDeckIndexer(DeckStore store)
{
    /// <summary>
    /// Writes the current state of a deck to the read model: a row for a public deck, no row for the other decks.
    /// </summary>
    /// <param name="ownerId">The owner of the deck.</param>
    /// <param name="deckId">The deck.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the read model contains the change.</returns>
    public async Task IndexAsync(Guid ownerId, Guid deckId, CancellationToken cancellationToken)
    {
        DeckRow? deck;
        IReadOnlyList<DeckLineRow> lines = [];
        await using (var content = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken))
        {
            deck = await DeckStore.FindDeckAsync(content, ownerId, deckId, cancellationToken);
            if (deck is { Visibility: "public", VariantOf: null })
            {
                lines = await DeckStore.ListLinesAsync(content, deckId, cancellationToken);
            }
        }

        await using var users = await store.OpenUsersAsync(cancellationToken);
        if (deck is not { Visibility: "public", VariantOf: null })
        {
            await PublicDeckStore.DeleteAsync(users, deckId, cancellationToken);
            return;
        }

        var played = lines.Where(line => line.Zone is "commander" or "companion" or "main").ToList();
        await PublicDeckStore.UpsertAsync(users, new PublicDeckRow
        {
            DeckId = deck.Id,
            OwnerId = deck.OwnerId,
            Name = deck.Name,
            Format = deck.Format,
            Description = deck.Description,
            ColorIdentity = deck.ColorIdentity,
            CommanderIds = [.. lines.Where(line => line.Zone == "commander").Select(line => line.OracleId).Distinct()],
            CardIds = [.. played.Select(line => line.OracleId).Distinct()],
            CardCount = played.Sum(line => line.Quantity),
            DeckVersion = deck.Version,
            CreatedAt = deck.CreatedAt,
            UpdatedAt = deck.UpdatedAt,
        }, cancellationToken);
    }

    /// <summary>
    /// Records a copy of a deck. The number of copies gives the popularity of a public deck.
    /// </summary>
    public async Task AddCopyAsync(Guid copyDeckId, Guid sourceDeckId, CancellationToken cancellationToken)
    {
        await using var users = await store.OpenUsersAsync(cancellationToken);
        await PublicDeckStore.AddCopyAsync(users, copyDeckId, sourceDeckId, cancellationToken);
    }

    /// <summary>
    /// Removes a deleted deck from the read model, with its favorites and its copy link.
    /// </summary>
    public async Task RemoveAsync(Guid deckId, CancellationToken cancellationToken)
    {
        await using var users = await store.OpenUsersAsync(cancellationToken);
        await PublicDeckStore.DeleteDeckDataAsync(users, deckId, cancellationToken);
    }
}

/// <summary>
/// Receives the domain events of the decks for the read model of the public decks.
/// </summary>
internal sealed class PublicDeckConsumer(PublicDeckIndexer indexer) : IDomainEventConsumer
{
    private static readonly HashSet<string> Types = ["decks.deck.created", "decks.deck.updated", "decks.deck.deleted"];

    /// <inheritdoc />
    public bool Accepts(string type, int version) => version == 1 && Types.Contains(type);

    /// <inheritdoc />
    public async Task HandleAsync(DomainEventEnvelope domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        switch (domainEvent.Type)
        {
            case "decks.deck.created":
                var created = domainEvent.Read<DeckCreated>();
                if (created.SourceDeckId is { } sourceDeckId)
                {
                    await indexer.AddCopyAsync(created.DeckId, sourceDeckId, cancellationToken);
                }

                await indexer.IndexAsync(OwnerOf(domainEvent), created.DeckId, cancellationToken);
                break;
            case "decks.deck.updated":
                await indexer.IndexAsync(OwnerOf(domainEvent), domainEvent.Read<DeckUpdated>().DeckId, cancellationToken);
                break;
            case "decks.deck.deleted":
                await indexer.RemoveAsync(domainEvent.Read<DeckDeleted>().DeckId, cancellationToken);
                break;
        }
    }

    private static Guid OwnerOf(DomainEventEnvelope domainEvent) =>
        domainEvent.OwnerId ?? throw new InvalidOperationException($"The event {domainEvent.Id} has no owner.");
}
