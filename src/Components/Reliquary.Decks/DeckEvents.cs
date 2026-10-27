using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Decks;

/// <summary>A user made a new deck.</summary>
/// <param name="DeckId">The ID of the deck.</param>
/// <param name="Format">The format of the deck.</param>
/// <param name="Visibility">The visibility of the deck.</param>
/// <param name="SourceDeckId">The deck that the user copied, or <see langword="null"/>.</param>
[DomainEvent("decks.deck.created", 1)]
public sealed record DeckCreated(Guid DeckId, string Format, string Visibility, Guid? SourceDeckId) : IDomainEvent;

/// <summary>A user changed a deck or its lines.</summary>
/// <param name="DeckId">The ID of the deck.</param>
/// <param name="Version">The new version of the deck.</param>
/// <param name="Format">The format of the deck.</param>
/// <param name="Visibility">The visibility of the deck.</param>
[DomainEvent("decks.deck.updated", 1)]
public sealed record DeckUpdated(Guid DeckId, int Version, string Format, string Visibility) : IDomainEvent;

/// <summary>A user deleted a deck.</summary>
/// <param name="DeckId">The ID of the deck.</param>
[DomainEvent("decks.deck.deleted", 1)]
public sealed record DeckDeleted(Guid DeckId) : IDomainEvent;
