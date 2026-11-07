using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// The fields of a new location.
/// </summary>
internal sealed record NewLocation(string Name, string? Kind, Guid? ParentId, bool Pickable, bool Locked, int Position);

/// <summary>
/// The changes of a location. A <see langword="null"/> field does not change.
/// </summary>
internal sealed record LocationChanges(string? Name, string? Kind, bool? Pickable, int? Position);

/// <summary>
/// The operations on the locations: list, create, change, lock, unlock and delete.
/// </summary>
internal sealed class LocationService(CollectionStore store, IDomainEventWriter events, TimeProvider time)
{
    public async Task<IReadOnlyList<LocationSummaryRow>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await CollectionStore.ListLocationsAsync(connection, ownerId, cancellationToken);
    }

    public async Task<LocationRow> CreateAsync(Guid ownerId, NewLocation input, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var kind = input.Kind;
        if (input.ParentId is { } parentId)
        {
            var parent = await CollectionStore.FindLocationAsync(connection, ownerId, parentId, cancellationToken, transaction, forUpdate: true);
            if (parent is null)
            {
                throw CollectionException.Validation(new FieldError("parentId", FieldErrorCodes.NotAllowed, "The parent location does not exist."));
            }

            if (parent.ParentId is not null)
            {
                throw CollectionException.Validation(new FieldError("parentId", FieldErrorCodes.NotAllowed, "A section cannot have a section. Give a location that has no parent."));
            }

            if (parent.DeckId is not null)
            {
                throw CollectionException.DeckLocation();
            }

            if (kind is not null && kind != parent.Kind)
            {
                throw CollectionException.Validation(new FieldError("kind", FieldErrorCodes.NotAllowed, $"A section has the kind of its parent location: \"{parent.Kind}\"."));
            }

            kind = parent.Kind;
        }
        else if (kind is null)
        {
            throw CollectionException.Validation(new FieldError("kind", FieldErrorCodes.Required, "The kind of the location is necessary."));
        }

        if (await CollectionStore.CountLocationsAsync(connection, ownerId, transaction, cancellationToken) >= CollectionRules.MaxLocations)
        {
            throw new CollectionException(StatusCodes.Status409Conflict, ErrorCodes.Conflict, $"You have {CollectionRules.MaxLocations} locations. Delete a location, then try again.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var location = new LocationRow
        {
            Id = Ids.New(),
            OwnerId = ownerId,
            ParentId = input.ParentId,
            Name = input.Name,
            Kind = kind,
            Locked = input.Locked,
            Pickable = input.Pickable,
            Position = input.Position,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await CollectionStore.InsertLocationAsync(transaction, location, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new LocationCreated(location.Id, location.Kind, location.ParentId, null), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return location;
    }

    public async Task<LocationRow> UpdateAsync(Guid ownerId, Guid locationId, LocationChanges changes, CancellationToken cancellationToken)
    {
        return await ChangeAsync(ownerId, locationId, location =>
        {
            if (location.DeckId is not null && (changes.Name is not null || changes.Kind is not null || changes.Pickable is not null))
            {
                throw CollectionException.DeckLocation();
            }

            if (changes.Kind is not null && changes.Kind != location.Kind && location.ParentId is not null)
            {
                throw CollectionException.Validation(new FieldError("kind", FieldErrorCodes.NotAllowed, "A section has the kind of its parent location. Change the kind of the parent location."));
            }

            location.Name = changes.Name ?? location.Name;
            location.Kind = changes.Kind ?? location.Kind;
            location.Pickable = changes.Pickable ?? location.Pickable;
            location.Position = changes.Position ?? location.Position;
        }, cancellationToken);
    }

    /// <summary>
    /// Locks or unlocks a location. A locked location refuses each change of its content.
    /// </summary>
    public Task<LocationRow> SetLockedAsync(Guid ownerId, Guid locationId, bool locked, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, locationId, location => location.Locked = locked, cancellationToken);

    /// <summary>
    /// Deletes an empty location and its empty sections.
    /// </summary>
    public async Task DeleteAsync(Guid ownerId, Guid locationId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var location = await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken, transaction, forUpdate: true)
            ?? throw CollectionException.LocationNotFound();
        if (location.DeckId is not null)
        {
            throw CollectionException.DeckLocation();
        }

        if (location.EffectiveLocked)
        {
            throw CollectionException.Locked(location.FullName);
        }

        if (await CollectionStore.CountCardsAsync(connection, ownerId, locationId, cancellationToken, transaction) > 0)
        {
            throw new CollectionException(
                StatusCodes.Status409Conflict,
                CollectionErrorCodes.LocationNotEmpty,
                "The location or one of its sections has cards. Move or remove the cards, then delete the location.");
        }

        var sections = await CollectionStore.ListSectionsAsync(connection, ownerId, locationId, cancellationToken, transaction);
        await CollectionStore.DeleteLocationAsync(transaction, ownerId, locationId, cancellationToken);
        foreach (var section in sections)
        {
            await events.WriteAsync(DatabaseTarget.Content, transaction, new LocationDeleted(section.Id), ownerId, cancellationToken);
        }

        await events.WriteAsync(DatabaseTarget.Content, transaction, new LocationDeleted(locationId), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a location of an owner.
    /// </summary>
    public async Task<LocationRow> GetAsync(Guid ownerId, Guid locationId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken) ?? throw CollectionException.LocationNotFound();
    }

    private async Task<LocationRow> ChangeAsync(Guid ownerId, Guid locationId, Action<LocationRow> change, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var location = await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken, transaction, forUpdate: true)
            ?? throw CollectionException.LocationNotFound();
        change(location);
        if (location.Kind == LocationKinds.Deck && location.DeckId is null)
        {
            throw CollectionException.Validation(new FieldError("kind", FieldErrorCodes.NotAllowed, "Only an assembled deck has the kind \"deck\"."));
        }

        location.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await CollectionStore.UpdateLocationAsync(transaction, location, cancellationToken);
        await events.WriteAsync(DatabaseTarget.Content, transaction, new LocationUpdated(location.Id, location.Kind, location.Locked, location.Pickable), ownerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return location;
    }
}
