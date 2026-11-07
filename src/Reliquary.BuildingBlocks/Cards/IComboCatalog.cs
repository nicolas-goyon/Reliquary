namespace Reliquary.BuildingBlocks.Cards;

/// <summary>
/// Gives the combos of Commander Spellbook to the core components. The Cards component implements it.
/// </summary>
/// <remarks>
/// The Cards component imports the combos with the card data. See ADR 0030.
/// The other core components use this interface, and never the tables of the Cards component.
/// </remarks>
public interface IComboCatalog
{
    /// <summary>
    /// Finds the combos that a group of cards makes, and the combos that need one more card.
    /// </summary>
    /// <param name="oracleIds">The IDs of the Oracle cards, for example the cards of a deck.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>
    /// Each combo that uses at least one of the cards and needs one other distinct card at most.
    /// The caller compares the quantities of the cards.
    /// </returns>
    Task<ComboSearch> FindCombosAsync(IReadOnlyCollection<Guid> oracleIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of a combo search.
/// </summary>
/// <param name="Available">
/// <see langword="true"/> when the instance has combo data.
/// When it is <see langword="false"/>, the search did not check the combos and <paramref name="Combos"/> is empty.
/// </param>
/// <param name="Combos">The combos that the search found.</param>
public sealed record ComboSearch(bool Available, IReadOnlyList<ComboFacts> Combos);

/// <summary>
/// One combo of Commander Spellbook.
/// </summary>
/// <param name="Id">The ID of the combo on Commander Spellbook, for example <c>742-1295</c>.</param>
/// <param name="Cards">The cards of the combo, in the order of Commander Spellbook.</param>
/// <param name="Results">The names of the results, for example <c>Win the game</c>.</param>
/// <param name="GameEnding">
/// <see langword="true"/> when one result is infinite or near-infinite,
/// or when Commander Spellbook marks a result as a standalone result, for example <c>Win the game</c>.
/// </param>
/// <param name="ManaNeeded">The mana that the combo needs, as Commander Spellbook gives it, for example <c>{U}{U}{B}</c>, or <see langword="null"/>.</param>
/// <param name="ManaValueNeeded">The mana value of <paramref name="ManaNeeded"/>.</param>
/// <param name="Popularity">The popularity of the combo. EDHREC gives it to Commander Spellbook. The value is <see langword="null"/> when EDHREC gives no value.</param>
public sealed record ComboFacts(
    string Id,
    IReadOnlyList<ComboCard> Cards,
    IReadOnlyList<string> Results,
    bool GameEnding,
    string? ManaNeeded,
    int ManaValueNeeded,
    int? Popularity)
{
    /// <summary>The address of the page of a combo on the Commander Spellbook site, without the ID.</summary>
    public const string PageAddress = "https://commanderspellbook.com/combo/";

    /// <summary>
    /// Gets the address of the page of the combo on Commander Spellbook.
    /// </summary>
    public Uri Url => new($"{PageAddress}{Uri.EscapeDataString(Id)}/");
}

/// <summary>
/// One card of a combo.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Quantity">The number of copies that the combo needs. It is almost always 1.</param>
public sealed record ComboCard(Guid OracleId, int Quantity);
