using System.Text;
using System.Text.RegularExpressions;

namespace Reliquary.Platform;

/// <summary>
/// Gives a 64-bit hash that stays the same between processes, for the keys of advisory locks.
/// </summary>
internal static class StableHash
{
    /// <summary>
    /// Gets the FNV-1a hash of a text.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The hash as a signed 64-bit integer.</returns>
    public static long Of(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }

        return unchecked((long)hash);
    }
}

/// <summary>
/// Checks the names of database schemas.
/// </summary>
internal static partial class SchemaName
{
    /// <summary>
    /// Tells whether a schema name is safe to put in SQL text.
    /// </summary>
    /// <param name="schema">The schema name.</param>
    /// <returns><see langword="true"/> when the name has only lowercase letters, digits and underscores, and starts with a letter.</returns>
    public static bool IsValid(string schema) => Pattern().IsMatch(schema);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex Pattern();
}
