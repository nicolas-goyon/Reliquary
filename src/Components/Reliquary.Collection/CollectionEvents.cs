using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Collection;

/// <summary>A user or the Decks component created a location.</summary>
/// <param name="LocationId">The ID of the location.</param>
/// <param name="Kind">The kind of the location.</param>
/// <param name="ParentId">The parent location of a section, or <see langword="null"/>.</param>
/// <param name="DeckId">The deck of an assembled deck location, or <see langword="null"/>.</param>
[DomainEvent("collection.location.created", 1)]
public sealed record LocationCreated(Guid LocationId, string Kind, Guid? ParentId, Guid? DeckId) : IDomainEvent;

/// <summary>A user changed the name, the kind, the order or the flags of a location.</summary>
/// <param name="LocationId">The ID of the location.</param>
/// <param name="Kind">The kind of the location.</param>
/// <param name="Locked">Whether the location is locked.</param>
/// <param name="Pickable">Whether the location is pickable.</param>
[DomainEvent("collection.location.updated", 1)]
public sealed record LocationUpdated(Guid LocationId, string Kind, bool Locked, bool Pickable) : IDomainEvent;

/// <summary>A user deleted a location, or the Decks component released the location of a deck.</summary>
/// <param name="LocationId">The ID of the location.</param>
[DomainEvent("collection.location.deleted", 1)]
public sealed record LocationDeleted(Guid LocationId) : IDomainEvent;

/// <summary>The entries of one or more locations changed. The movements of the batch give the changes.</summary>
/// <param name="BatchId">The batch ID of the movements.</param>
/// <param name="LocationIds">The locations that changed.</param>
/// <param name="OracleIds">The Oracle cards that changed.</param>
[DomainEvent("collection.entries.changed", 1)]
public sealed record EntriesChanged(Guid BatchId, IReadOnlyList<Guid> LocationIds, IReadOnlyList<Guid> OracleIds) : IDomainEvent;
