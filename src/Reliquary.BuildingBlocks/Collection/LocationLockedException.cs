namespace Reliquary.BuildingBlocks.Collection;

/// <summary>
/// A change of the content of a locked location. The user must unlock the location first.
/// The Collection component gives this error to the other core components, for example through <see cref="IAssembledDeckLocations"/>.
/// </summary>
/// <param name="locationId">The locked location.</param>
public sealed class LocationLockedException(Guid locationId)
    : Exception($"The location {locationId} is locked. Unlock the location, then try again.")
{
    /// <summary>The stable error code of the public API.</summary>
    public const string Code = "location_locked";

    /// <summary>Gets the locked location.</summary>
    public Guid LocationId { get; } = locationId;
}
