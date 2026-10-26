using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Platform.Migrations;

/// <summary>
/// The migration sets of the platform: the shard directory, the outbox tables and the job runs.
/// </summary>
public static class PlatformMigrations
{
    /// <summary>The owner name of the platform sets.</summary>
    public const string Owner = "platform";

    /// <summary>
    /// Gets the migration sets of the platform. The runner applies them before the sets of the core components.
    /// </summary>
    public static IReadOnlyList<MigrationSet> Sets { get; } =
    [
        new(DatabaseTarget.Users, "users", Owner, typeof(PlatformMigrations).Assembly),
        new(DatabaseTarget.Reference, "reference", Owner, typeof(PlatformMigrations).Assembly),
        new(DatabaseTarget.Content, "content", Owner, typeof(PlatformMigrations).Assembly),
    ];
}
