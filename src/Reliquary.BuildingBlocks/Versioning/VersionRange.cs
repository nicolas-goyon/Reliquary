namespace Reliquary.BuildingBlocks.Versioning;

/// <summary>
/// A range of versions, in the syntax of npm: for example <c>^1.0</c>, <c>~1.2.0</c>, <c>&gt;=1.0.0 &lt;2.0.0</c>, <c>1.x</c> or <c>*</c>.
/// </summary>
/// <remarks>
/// A range is one or more sets, separated by <c>||</c>. A version is in the range when it is in one set.
/// A set is one or more comparisons, separated by spaces. A version is in a set when it agrees with all the comparisons.
/// </remarks>
public sealed class VersionRange
{
    private readonly IReadOnlyList<IReadOnlyList<(string Operator, SemanticVersion Version)>> sets;

    private VersionRange(string text, IReadOnlyList<IReadOnlyList<(string, SemanticVersion)>> sets)
    {
        Text = text;
        this.sets = sets;
    }

    /// <summary>Gets the text of the range.</summary>
    public string Text { get; }

    /// <summary>
    /// Reads a range.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="range">The range.</param>
    /// <returns><see langword="true"/> when the text is a valid range.</returns>
    public static bool TryParse(string? text, out VersionRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var sets = new List<IReadOnlyList<(string, SemanticVersion)>>();
        foreach (var setText in text.Split("||"))
        {
            var comparisons = new List<(string, SemanticVersion)>();
            foreach (var token in setText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParseComparison(token, comparisons))
                {
                    return false;
                }
            }

            if (comparisons.Count == 0)
            {
                return false;
            }

            sets.Add(comparisons);
        }

        range = new VersionRange(text.Trim(), sets);
        return true;
    }

    /// <summary>
    /// Tells whether a version is in the range.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns><see langword="true"/> when the version is in the range.</returns>
    public bool Contains(SemanticVersion version) =>
        sets.Any(set => set.All(comparison => comparison.Operator switch
        {
            ">=" => version >= comparison.Version,
            ">" => version > comparison.Version,
            "<=" => version <= comparison.Version,
            "<" => version < comparison.Version,
            _ => version.CompareTo(comparison.Version) == 0,
        }));

    /// <inheritdoc />
    public override string ToString() => Text;

    private static bool TryParseComparison(string token, List<(string, SemanticVersion)> comparisons)
    {
        if (token is "*" or "x" or "X")
        {
            comparisons.Add((">=", new SemanticVersion(0, 0, 0)));
            return true;
        }

        var prefix = token switch
        {
            _ when token.StartsWith(">=", StringComparison.Ordinal) => ">=",
            _ when token.StartsWith("<=", StringComparison.Ordinal) => "<=",
            _ when token.StartsWith('>') => ">",
            _ when token.StartsWith('<') => "<",
            _ when token.StartsWith('^') => "^",
            _ when token.StartsWith('~') => "~",
            _ when token.StartsWith('=') => "=",
            _ => string.Empty,
        };
        var versionText = token[prefix.Length..];
        if (!TryParsePartial(versionText, out var major, out var minor, out var patch, out var preRelease))
        {
            return false;
        }

        var lower = new SemanticVersion(major, minor ?? 0, patch ?? 0, preRelease);
        switch (prefix)
        {
            case "^":
                comparisons.Add((">=", lower));
                comparisons.Add(("<", major > 0 || minor is null
                    ? new SemanticVersion(major + 1, 0, 0)
                    : minor > 0 || patch is null ? new SemanticVersion(0, minor.Value + 1, 0) : new SemanticVersion(0, 0, patch.Value + 1)));
                return true;
            case "~":
                comparisons.Add((">=", lower));
                comparisons.Add(("<", minor is null ? new SemanticVersion(major + 1, 0, 0) : new SemanticVersion(major, minor.Value + 1, 0)));
                return true;
            case "" or "=" when minor is null || patch is null:
                // A partial version is a range: "1" means 1.x.x, and "1.2" means 1.2.x.
                comparisons.Add((">=", lower));
                comparisons.Add(("<", minor is null ? new SemanticVersion(major + 1, 0, 0) : new SemanticVersion(major, minor.Value + 1, 0)));
                return true;
            case "" or "=":
                comparisons.Add(("=", lower));
                return true;
            case "<=" when minor is null || patch is null:
                comparisons.Add(("<", minor is null ? new SemanticVersion(major + 1, 0, 0) : new SemanticVersion(major, minor.Value + 1, 0)));
                return true;
            case ">" when minor is null || patch is null:
                comparisons.Add((">=", minor is null ? new SemanticVersion(major + 1, 0, 0) : new SemanticVersion(major, minor.Value + 1, 0)));
                return true;
            default:
                comparisons.Add((prefix, lower));
                return true;
        }
    }

    private static bool TryParsePartial(string text, out int major, out int? minor, out int? patch, out string preRelease)
    {
        (major, minor, patch, preRelease) = (0, null, null, string.Empty);
        if (SemanticVersion.TryParse(text, out var full))
        {
            (major, minor, patch, preRelease) = (full.Major, full.Minor, full.Patch, full.PreRelease);
            return true;
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 3 || !SemanticVersion.TryNumber(parts[0], out major))
        {
            return false;
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (parts[index] is "x" or "X" or "*")
            {
                // A wildcard ends the version: "1.x" and "1.x.x" are the same as "1".
                return parts.Skip(index + 1).All(part => part is "x" or "X" or "*");
            }

            if (!SemanticVersion.TryNumber(parts[index], out var number))
            {
                return false;
            }

            if (index == 1)
            {
                minor = number;
            }
            else
            {
                patch = number;
            }
        }

        return true;
    }
}
