namespace Reliquary.BuildingBlocks.Data;

/// <summary>
/// A group of databases that contain the same schemas. See docs/architecture/database.md.
/// </summary>
public enum DatabaseTarget
{
    /// <summary>
    /// The database of the <c>users</c> schema: accounts, API keys, quotas and the shard directory.
    /// </summary>
    Users,

    /// <summary>
    /// The database of the <c>reference</c> schema: the card reference data that all users share.
    /// </summary>
    Reference,

    /// <summary>
    /// The shard databases of the <c>content</c> schema: the data of the users, with an <c>owner_id</c> on each row.
    /// </summary>
    Content,
}
