using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Versions;

/// <summary>
/// The assembled version of a deck: the version that agrees with the physical cards. See docs/decisions/0033-deck-versions-and-variants.md.
/// </summary>
/// <remarks>
/// The location of an assembled deck belongs to the Collection component. This service gives it the cards through <see cref="IAssembledDeckLocations"/>.
/// </remarks>
internal sealed class AssembledDeckService(
    DeckStore store,
    DeckService decks,
    DeckVersionService versions,
    IAssembledDeckLocations locations,
    ICardCatalog catalog,
    IDomainEventWriter events,
    TimeProvider time)
{
    /// <summary>The zones of the physical deck. The considering zone and the tokens are not in the location.</summary>
    public static readonly IReadOnlyList<string> PhysicalZones = ["commander", "companion", "main", "sideboard"];

    /// <summary>
    /// Marks a version as the assembled version, and gives its cards to the location of the deck.
    /// With no version, the method uses the most recent version, after a commit of the working list when it changed.
    /// </summary>
    /// <exception cref="DeckException">The deck is a variant, the deck does not exist, or the version is not a version of the deck.</exception>
    public async Task<DeckRow> AssembleAsync(Guid ownerId, Guid deckId, Guid? versionId, CancellationToken cancellationToken)
    {
        var deck = await OwnDeckAsync(ownerId, deckId, cancellationToken);
        if (deck.VariantOf is not null)
        {
            throw DeckException.Validation(new FieldError("deckId", FieldErrorCodes.NotAllowed, "A variant cannot be an assembled deck. Merge the variant, then assemble the main deck."));
        }

        var targetId = versionId;
        if (targetId is null)
        {
            var (_, changed, current) = await versions.ListAsync(ownerId, deckId, cancellationToken);
            targetId = changed || current.HeadVersionId is null
                ? (await versions.CommitAsync(ownerId, deckId, "Assembled version", cancellationToken)).Id
                : current.HeadVersionId;
        }

        var (_, lines, _) = await versions.GetAsync(ownerId, deckId, targetId.Value, cancellationToken);
        var physical = lines.Where(line => PhysicalZones.Contains(line.Zone)).ToList();
        var printings = await catalog.GetPrintingsAsync([.. physical.Select(line => line.PrintingId).OfType<Guid>().Distinct()], cancellationToken);
        var cards = physical
            .GroupBy(line => (line.OracleId, line.PrintingId, line.Finish))
            .Select(group => new AssembledDeckCard(
                group.Key.OracleId,
                group.Key.PrintingId,
                group.Key.PrintingId is { } printingId && printings.TryGetValue(printingId, out var printing) ? printing.Lang : "en",
                group.Key.Finish,
                group.Sum(line => line.Quantity)))
            .ToList();

        await locations.EnsureLocationAsync(ownerId, deckId, deck.Name, cancellationToken);
        await locations.SetContentAsync(ownerId, deckId, cards, cancellationToken);
        return await SetAssembledVersionAsync(ownerId, deckId, targetId, cancellationToken);
    }

    /// <summary>
    /// Releases an assembled deck: its cards go to another location, or leave the collection.
    /// </summary>
    public async Task<DeckRow> ReleaseAsync(Guid ownerId, Guid deckId, Guid? targetLocationId, CancellationToken cancellationToken)
    {
        var deck = await OwnDeckAsync(ownerId, deckId, cancellationToken);
        if (deck.AssembledVersionId is null)
        {
            throw DeckException.Validation(new FieldError("deckId", FieldErrorCodes.NotAllowed, "The deck is not an assembled deck."));
        }

        await locations.ReleaseAsync(ownerId, deckId, targetLocationId, cancellationToken);
        return await SetAssembledVersionAsync(ownerId, deckId, null, cancellationToken);
    }

    /// <summary>
    /// Gives the new name of an assembled deck to its location.
    /// </summary>
    public async Task RenameLocationAsync(DeckRow deck, CancellationToken cancellationToken)
    {
        if (deck.AssembledVersionId is not null)
        {
            await locations.EnsureLocationAsync(deck.OwnerId, deck.Id, deck.Name, cancellationToken);
        }
    }

    private async Task<DeckRow> OwnDeckAsync(Guid ownerId, Guid deckId, CancellationToken cancellationToken)
    {
        var deck = (await decks.GetAsync(ownerId, deckId, cancellationToken)).Deck;
        return deck.OwnerId == ownerId ? deck : throw DeckException.NotFound();
    }

    private async Task<DeckRow> SetAssembledVersionAsync(Guid ownerId, Guid deckId, Guid? versionId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var deck = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken, transaction, forUpdate: true) ?? throw DeckException.NotFound();
        deck.AssembledVersionId = versionId;
        deck.Version++;
        deck.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await DeckStore.UpdateDeckAsync(transaction, deck, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckAssembled(deck.Id, versionId), ownerId, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new DeckUpdated(deck.Id, deck.Version, deck.Format, deck.Visibility, deck.VariantOf), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deck;
    }
}

/// <summary>
/// The error codes of the assembled decks.
/// </summary>
public static class AssembledDeckErrorCodes
{
    /// <summary>The deck is an assembled deck, so the operation must release it first.</summary>
    public const string DeckAssembled = "deck_assembled";

    /// <summary>The operation needs an assembled deck, and the deck has no assembled version.</summary>
    public const string DeckNotAssembled = "deck_not_assembled";

    /// <summary>Creates the error of a transition plan for a deck that is not an assembled deck.</summary>
    internal static DeckException NotAssembled() =>
        new(StatusCodes.Status409Conflict, DeckNotAssembled, "The deck is not an assembled deck. Mark a version of the main deck as the assembled version first.");

    /// <summary>Creates the error of the deletion of an assembled deck.</summary>
    internal static DeckException DeletionRefused() =>
        new(StatusCodes.Status409Conflict, DeckAssembled, "The deck is an assembled deck. Release it first: tell where its cards go, then delete the deck.");
}
