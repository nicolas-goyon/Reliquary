using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Reliquary.BuildingBlocks.Versioning;

/// <summary>
/// A version in the Semantic Versioning 2.0 format: <c>major.minor.patch</c>, with an optional pre-release part.
/// </summary>
/// <param name="Major">The major version. It increases for a change that breaks compatibility.</param>
/// <param name="Minor">The minor version. It increases for a compatible new feature.</param>
/// <param name="Patch">The patch version. It increases for a compatible correction.</param>
/// <param name="PreRelease">The pre-release part, for example <c>beta.1</c>, or an empty text.</param>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string PreRelease = "") : IComparable<SemanticVersion>
{
    /// <summary>
    /// Reads a full version, for example <c>1.4.0</c> or <c>2.0.0-beta.1</c>. Build metadata after a <c>+</c> sign is ignored.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="version">The version.</param>
    /// <returns><see langword="true"/> when the text is a full version.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var preRelease = string.Empty;
        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
            if (preRelease.Length == 0 || preRelease.Split('.').Any(part => part.Length == 0 || !part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            {
                return false;
            }
        }

        var parts = value.Split('.');
        if (parts.Length != 3 || !TryNumber(parts[0], out var major) || !TryNumber(parts[1], out var minor) || !TryNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    /// <summary>
    /// Reads a full version.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The version.</returns>
    /// <exception cref="FormatException">The text is not a full version.</exception>
    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"\"{text}\" is not a semantic version. Use the format major.minor.patch, for example 1.0.0.");

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        result = result != 0 ? result : Minor.CompareTo(other.Minor);
        result = result != 0 ? result : Patch.CompareTo(other.Patch);
        return result != 0 ? result : ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>Compares two versions.</summary>
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    /// <summary>Compares two versions.</summary>
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    /// <summary>Compares two versions.</summary>
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two versions.</summary>
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => PreRelease.Length == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    internal static bool TryNumber(string text, out int value)
    {
        value = 0;
        return text.Length > 0 && (text.Length == 1 || text[0] != '0') && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static int ComparePreRelease(string left, string right)
    {
        // A version with no pre-release part comes after its pre-releases.
        if (left.Length == 0 || right.Length == 0)
        {
            return (left.Length, right.Length) switch
            {
                (0, 0) => 0,
                (0, _) => 1,
                _ => -1,
            };
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var leftNumeric = int.TryParse(leftParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(rightParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            var result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftParts[index], rightParts[index]),
            };
            if (result != 0)
            {
                return result;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
