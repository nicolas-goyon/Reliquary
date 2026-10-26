namespace Reliquary.Platform.Data;

/// <summary>
/// The settings of the databases. The section is <c>Database</c>.
/// </summary>
/// <remarks>
/// The connection strings come from the <c>ConnectionStrings</c> section:
/// <list type="bullet">
/// <item><c>ConnectionStrings:Users</c> is necessary.</item>
/// <item><c>ConnectionStrings:Reference</c> is optional. Without it, the reference data uses the users database.</item>
/// </list>
/// </remarks>
public sealed class DatabaseOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Database";

    /// <summary>The name of the default shard when the configuration declares no shard.</summary>
    public const string MainShard = "main";

    /// <summary>
    /// Gets or sets the connection string of each shard database, by shard name.
    /// Without a shard, the instance has one shard, <c>main</c>, in the users database.
    /// </summary>
    public Dictionary<string, string> Shards { get; set; } = [];

    /// <summary>
    /// Gets or sets the shard of the new owners.
    /// </summary>
    public string DefaultShard { get; set; } = MainShard;

    /// <summary>
    /// Gets or sets whether the host applies the migrations when it starts.
    /// </summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// Gets or sets the time that the shard directory keeps an assignment in memory.
    /// </summary>
    public TimeSpan DirectoryCacheDuration { get; set; } = TimeSpan.FromMinutes(1);
}
