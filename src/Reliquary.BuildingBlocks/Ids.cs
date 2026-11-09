namespace Reliquary.BuildingBlocks;

/// <summary>
/// Creates the identifiers of all rows.
/// </summary>
/// <remarks>
/// All identifiers are UUIDv7 values. Their order follows the time of creation,
/// and a copy of a row to another database causes no collision. See ADR 0008.
/// </remarks>
public static class Ids
{
    /// <summary>
    /// Creates a new UUIDv7 identifier.
    /// </summary>
    /// <returns>A new identifier.</returns>
    public static Guid New() => Guid.CreateVersion7();

    /// <summary>
    /// Creates a new UUIDv7 identifier with the current time of a time provider.
    /// </summary>
    /// <param name="time">The time provider. A test can give a fake time provider to get a known order.</param>
    /// <returns>A new identifier.</returns>
    public static Guid New(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return Guid.CreateVersion7(time.GetUtcNow());
    }
}
