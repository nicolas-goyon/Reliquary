using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Decks;

/// <summary>A user made a new deck.</summary>
/// <param name="DeckId">The ID of the deck.</param>
/// <param name="Format">The format of the deck.</param>
/// <param name="Visibility">The visibility of the deck.</param>
/// <param name="SourceDeckId">The deck that the user copied, or <see langword="null"/>.</param>
/// <param name="VariantOf">The main deck when the deck is a variant, or <see langword="null"/>.</param>
[DomainEvent("decks.deck.created", 1)]
public sealed record DeckCreated(Guid DeckId, string Format, string Visibility, Guid? SourceDeckId, Guid? VariantOf = null) : IDomainEvent;

/// <summary>A user changed a deck or its lines.</summary>
/// <param name="DeckId">The ID of the deck.</param>
/// <param name="Version">The new version of the deck.</param>
/// <param name="Format">The format of the deck.</param>
/// <param name="Visibility">The visibility of the deck.</param>
/// <param name="VariantOf">The main deck when the deck is a variant, or <see langword="null"/>.</param>
[DomainEvent("decks.deck.updated", 1)]
public sealed record DeckUpdated(Guid DeckId, int Version, string Format, string Visibility, Guid? VariantOf = null) : IDomainEvent;

/// <summary>A user deleted a deck.</summary>
/// <param name="DeckId">The ID of the deck.</param>
[DomainEvent("decks.deck.deleted", 1)]
public sealed record DeckDeleted(Guid DeckId) : IDomainEvent;

/// <summary>A user made a new version of a deck: a commit, a merge, a revert or a restore.</summary>
/// <param name="DeckId">The ID of the deck or of the variant.</param>
/// <param name="VersionId">The ID of the version.</param>
/// <param name="Number">The number of the version in its deck.</param>
/// <param name="Kind">The kind: <c>commit</c>, <c>merge</c>, <c>revert</c> or <c>restore</c>.</param>
[DomainEvent("decks.deck_version.created", 1)]
public sealed record DeckVersionCreated(Guid DeckId, Guid VersionId, int Number, string Kind) : IDomainEvent;

/// <summary>A user changed the assembled version of a deck, or released the assembled deck.</summary>
/// <param name="DeckId">The ID of the deck.</param>
/// <param name="AssembledVersionId">The assembled version, or <see langword="null"/> when the deck is not assembled any more.</param>
[DomainEvent("decks.deck.assembled", 1)]
public sealed record DeckAssembled(Guid DeckId, Guid? AssembledVersionId) : IDomainEvent;
