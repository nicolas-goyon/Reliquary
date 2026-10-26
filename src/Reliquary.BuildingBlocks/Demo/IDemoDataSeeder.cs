namespace Reliquary.BuildingBlocks.Demo;

/// <summary>
/// Loads the demo data of a core component into a new instance. The command <c>reliquary demo load</c> runs all the seeders.
/// </summary>
/// <remarks>
/// A seeder must give the same result when it runs two times. It must not change data that a user made.
/// </remarks>
public interface IDemoDataSeeder
{
    /// <summary>Gets the name of the seeder, for example <c>accounts</c>.</summary>
    string Name { get; }

    /// <summary>Gets the position of the seeder. A small number runs first.</summary>
    int Order { get; }

    /// <summary>
    /// Loads the demo data.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A short description of the result, for the administrator.</returns>
    Task<string> SeedAsync(CancellationToken cancellationToken);
}
