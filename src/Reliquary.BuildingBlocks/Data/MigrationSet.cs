using System.Reflection;
using System.Text;

namespace Reliquary.BuildingBlocks.Data;

/// <summary>
/// An ordered list of SQL scripts that one owner applies to one schema.
/// </summary>
/// <remarks>
/// The owner is a core component, the platform or a module.
/// Each owner has its own sets, so two owners never share a script number.
/// The scripts are embedded resources with the logical name <c>Migrations/&lt;schema&gt;/&lt;file&gt;.sql</c>.
/// </remarks>
/// <param name="Target">The databases that receive the scripts.</param>
/// <param name="Schema">The schema of the tables, for example <c>users</c> or <c>mod_example</c>.</param>
/// <param name="Owner">The owner of the set, in lowercase, for example <c>accounts</c>.</param>
/// <param name="Assembly">The assembly that contains the scripts.</param>
public sealed record MigrationSet(DatabaseTarget Target, string Schema, string Owner, Assembly Assembly)
{
    private const string Prefix = "Migrations/";

    /// <summary>
    /// Gets the scripts of the set, ordered by name.
    /// </summary>
    /// <returns>The name and the content of each script. The name has the form <c>&lt;owner&gt;/&lt;file&gt;</c>.</returns>
    public IReadOnlyList<(string Name, string Contents)> ReadScripts()
    {
        var folder = $"{Prefix}{Schema}/";
        var scripts = new List<(string Name, string Contents)>();

        foreach (var resource in Assembly.GetManifestResourceNames())
        {
            var normalized = resource.Replace('\\', '/');
            if (!normalized.StartsWith(folder, StringComparison.Ordinal) || !normalized.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The assembly {Assembly.GetName().Name} has no resource {resource}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            scripts.Add(($"{Owner}/{normalized[folder.Length..]}", reader.ReadToEnd()));
        }

        return [.. scripts.OrderBy(script => script.Name, StringComparer.Ordinal)];
    }
}
