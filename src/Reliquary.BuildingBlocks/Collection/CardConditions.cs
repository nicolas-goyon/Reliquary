namespace Reliquary.BuildingBlocks.Collection;

/// <summary>
/// The conditions of a physical card, from the best to the worst.
/// The location entries of the collection and the purchase preferences of the decks use the same scale.
/// </summary>
public static class CardConditions
{
    /// <summary>Mint.</summary>
    public const string Mint = "mint";

    /// <summary>Near mint.</summary>
    public const string NearMint = "near_mint";

    /// <summary>Excellent.</summary>
    public const string Excellent = "excellent";

    /// <summary>Good.</summary>
    public const string Good = "good";

    /// <summary>Light played.</summary>
    public const string LightPlayed = "light_played";

    /// <summary>Played.</summary>
    public const string Played = "played";

    /// <summary>Poor.</summary>
    public const string Poor = "poor";

    /// <summary>Gets all the conditions, from the best to the worst.</summary>
    public static IReadOnlyList<string> All { get; } = [Mint, NearMint, Excellent, Good, LightPlayed, Played, Poor];

    /// <summary>
    /// Gets the rank of a condition: 0 for <see cref="Mint"/>, then 1, 2 and more for a worse condition.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The rank.</returns>
    /// <exception cref="ArgumentException">The condition is not one of <see cref="All"/>.</exception>
    public static int Rank(string condition)
    {
        var rank = All.ToList().IndexOf(condition);
        return rank >= 0 ? rank : throw new ArgumentException($"The condition \"{condition}\" is not known.", nameof(condition));
    }
}
