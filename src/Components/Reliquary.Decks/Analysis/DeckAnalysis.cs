namespace Reliquary.Decks.Analysis;

/// <summary>
/// One deck line for the analysis.
/// </summary>
/// <param name="Zone">The zone of the line.</param>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Categories">The categories of the line, for example <c>ramp</c>.</param>
public sealed record AnalysisLine(string Zone, Guid OracleId, int Quantity, IReadOnlyList<string> Categories);

/// <summary>
/// The analysis of a deck. See docs/architecture/deck-analysis.md.
/// </summary>
/// <param name="Format">The format of the deck.</param>
/// <param name="CardCount">The number of cards in the commander, companion and main zones.</param>
/// <param name="LandCount">The number of lands, with the modal lands.</param>
/// <param name="ModalLandCount">The number of modal lands: modal double-faced cards with a spell face and a land face.</param>
/// <param name="SpellCount">The number of cards that are not lands. The modal lands are in this count too.</param>
/// <param name="AverageManaValue">The average mana value of the cards that are not lands, with two decimals.</param>
/// <param name="ManaCurve">The number of cards for each mana value from 0 to 7. The value 7 also counts the cards with a higher mana value.</param>
/// <param name="Types">The number of cards for each card type of the castable faces.</param>
/// <param name="Colors">The number of cards that are not lands for each color of the castable faces. <c>C</c> counts the colorless cards.</param>
/// <param name="Categories">The number of cards in each category, and the cards that are not lands and have no category.</param>
/// <param name="Mana">The mana sources and the mana requirements of each color.</param>
/// <param name="Draws">The probabilities to draw lands.</param>
/// <param name="Needs">The needs of each category for a Commander deck, or <see langword="null"/> for the other formats.</param>
/// <param name="Bracket">The indicative Commander bracket, or <see langword="null"/> for the other formats.</param>
/// <param name="Combos">The combos of Commander Spellbook in the deck, and the combos that need one more card.</param>
public sealed record DeckAnalysis(
    string Format,
    int CardCount,
    int LandCount,
    int ModalLandCount,
    int SpellCount,
    decimal AverageManaValue,
    IReadOnlyList<CurvePoint> ManaCurve,
    IReadOnlyList<CountEntry> Types,
    IReadOnlyList<CountEntry> Colors,
    IReadOnlyList<CountEntry> Categories,
    ManaAnalysis Mana,
    DrawAnalysis Draws,
    IReadOnlyList<CategoryNeed>? Needs,
    BracketAnalysis? Bracket,
    ComboAnalysis Combos);

/// <summary>
/// One point of the mana curve.
/// </summary>
/// <param name="ManaValue">The mana value. The value 7 also counts the cards with a higher mana value.</param>
/// <param name="Count">The number of cards.</param>
public sealed record CurvePoint(int ManaValue, int Count);

/// <summary>
/// A number of cards for a key of a distribution.
/// </summary>
/// <param name="Key">The stable key, for example <c>creature</c>, <c>W</c> or <c>ramp</c>.</param>
/// <param name="Count">The number of cards.</param>
public sealed record CountEntry(string Key, int Count);

/// <summary>
/// The mana sources and the mana requirements of a deck.
/// </summary>
/// <param name="TableDeckSize">The deck size of the table of Frank Karsten that the requirements use: 60 or 99.</param>
/// <param name="Colors">The result of each color that a card needs or that a source produces, in the order W, U, B, R, G, C.</param>
public sealed record ManaAnalysis(int TableDeckSize, IReadOnlyList<ColorMana> Colors);

/// <summary>
/// The mana sources and the mana requirement of one color.
/// </summary>
/// <param name="Color">The color: <c>W</c>, <c>U</c>, <c>B</c>, <c>R</c>, <c>G</c>, or <c>C</c> for colorless mana.</param>
/// <param name="Symbols">The number of mana symbols of this color in the primary mana costs of the cards.</param>
/// <param name="LandSources">The number of lands in the main zone that produce this color, with the modal lands.</param>
/// <param name="OtherSources">The number of other permanents in the main zone that produce this color.</param>
/// <param name="Sources">The sources for the card of the requirement: the lands, and a part of the other sources. See the documentation.</param>
/// <param name="Required">The number of sources that the card of the requirement needs, or <see langword="null"/> when no card needs this color.</param>
/// <param name="OracleId">The card with the highest need of sources, or <see langword="null"/>.</param>
/// <param name="Pips">The number of symbols of this color in the mana cost of that card.</param>
/// <param name="ManaValue">The mana value of that card.</param>
/// <param name="Status">The status: <c>ok</c>, <c>low</c>, or <c>no_requirement</c>.</param>
public sealed record ColorMana(
    string Color,
    int Symbols,
    int LandSources,
    int OtherSources,
    decimal Sources,
    int? Required,
    Guid? OracleId,
    int? Pips,
    decimal? ManaValue,
    string Status);

/// <summary>
/// The probabilities to draw lands from the library.
/// </summary>
/// <param name="LibrarySize">The number of cards in the library: the main zone.</param>
/// <param name="Lands">The number of lands in the library, with the modal lands.</param>
/// <param name="Checkpoints">The probabilities at each checkpoint.</param>
public sealed record DrawAnalysis(int LibrarySize, int Lands, IReadOnlyList<DrawCheckpoint> Checkpoints);

/// <summary>
/// The probabilities to have lands at one moment of the game.
/// </summary>
/// <param name="Code">The stable code, for example <c>opening_hand</c> or <c>turn_3_play</c>.</param>
/// <param name="Turn">The turn. The opening hand is turn 1 on the play.</param>
/// <param name="OnThePlay">Whether the player starts the game. A player on the play does not draw on turn 1.</param>
/// <param name="Cards">The number of cards that the player saw: 7, and one card for each draw.</param>
/// <param name="AtLeast">The probability to have at least each number of lands, from 1 to 7.</param>
public sealed record DrawCheckpoint(string Code, int Turn, bool OnThePlay, int Cards, IReadOnlyList<LandProbability> AtLeast);

/// <summary>
/// The probability to have at least a number of lands.
/// </summary>
/// <param name="Lands">The number of lands.</param>
/// <param name="Probability">The probability, from 0 to 1, with four decimals.</param>
public sealed record LandProbability(int Lands, double Probability);

/// <summary>
/// The need of one category in a Commander deck.
/// </summary>
/// <param name="Category">The category, for example <c>ramp</c>.</param>
/// <param name="Count">The number of cards in the category.</param>
/// <param name="Target">The target number of cards, or <see langword="null"/> when the category has no target.</param>
/// <param name="Min">The lowest number of cards with the status <c>ok</c>.</param>
/// <param name="Max">The highest number of cards with the status <c>ok</c>.</param>
/// <param name="Status">The status: <c>low</c>, <c>ok</c>, <c>high</c>, or <c>no_target</c>.</param>
/// <param name="Difference">The count minus the target. A negative difference gives the missing cards.</param>
public sealed record CategoryNeed(string Category, int Count, int? Target, int? Min, int? Max, string Status, int? Difference);

/// <summary>
/// The indicative Commander bracket of a deck.
/// </summary>
/// <param name="Bracket">The lowest bracket that the deck fits, from 1 to 4. The analysis never gives bracket 5, because bracket 5 is an intention.</param>
/// <param name="Reasons">The reasons for a bracket higher than 1.</param>
/// <param name="Unchecked">The rules that the analysis does not check, for example <c>two_card_combos</c> when the instance has no combo data.</param>
public sealed record BracketAnalysis(int Bracket, IReadOnlyList<BracketReason> Reasons, IReadOnlyList<string> Unchecked);

/// <summary>
/// One reason for a bracket.
/// </summary>
/// <param name="Code">The stable code: <c>game_changers</c>, <c>mass_land_denial</c>, <c>extra_turns</c>, <c>two_card_combos</c> or <c>late_two_card_combos</c>.</param>
/// <param name="Bracket">The lowest bracket that this reason permits.</param>
/// <param name="OracleIds">The cards of the reason.</param>
/// <param name="ComboIds">The IDs of the combos of the reason on Commander Spellbook. The list is empty for a reason that is not a combo.</param>
public sealed record BracketReason(string Code, int Bracket, IReadOnlyList<Guid> OracleIds, IReadOnlyList<string> ComboIds);

/// <summary>
/// The combos of Commander Spellbook in a deck. See ADR 0030.
/// </summary>
/// <param name="Available">
/// <see langword="true"/> when the instance has combo data.
/// When it is <see langword="false"/>, the analysis did not check the combos and the lists are empty.
/// </param>
/// <param name="Complete">The combos with all their cards in the deck, from the most popular.</param>
/// <param name="Near">The combos that need one more card, from the most popular. The list has <see cref="DeckCombos.MaxNearCombos"/> combos at most.</param>
/// <param name="NearCount">The number of combos that need one more card, also the combos that are not in <paramref name="Near"/>.</param>
public sealed record ComboAnalysis(bool Available, IReadOnlyList<DeckCombo> Complete, IReadOnlyList<DeckCombo> Near, int NearCount);

/// <summary>
/// One combo of Commander Spellbook for a deck.
/// </summary>
/// <param name="Id">The ID of the combo on Commander Spellbook, for example <c>742-1295</c>.</param>
/// <param name="Url">The page of the combo on Commander Spellbook.</param>
/// <param name="Cards">The cards of the combo.</param>
/// <param name="Results">The names of the results, in English, as Commander Spellbook gives them. For example <c>Win the game</c>.</param>
/// <param name="GameEnding">Whether a result is infinite or near-infinite, or a standalone result of Commander Spellbook such as <c>Win the game</c>.</param>
/// <param name="ManaNeeded">The mana that the combo needs, as Commander Spellbook gives it, for example <c>{U}{U}{B}</c>, or <see langword="null"/>.</param>
/// <param name="ManaValue">The mana value of the combo: the larger of the mana value of <paramref name="ManaNeeded"/> and the sum of the mana values of the cards.</param>
/// <param name="MissingOracleId">The card that the deck does not contain, for a combo that needs one more card. Otherwise <see langword="null"/>.</param>
public sealed record DeckCombo(
    string Id,
    Uri Url,
    IReadOnlyList<DeckComboCard> Cards,
    IReadOnlyList<string> Results,
    bool GameEnding,
    string? ManaNeeded,
    int ManaValue,
    Guid? MissingOracleId);

/// <summary>
/// One card of a combo.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The Oracle name of the card.</param>
/// <param name="Quantity">The number of copies that the combo needs.</param>
/// <param name="InDeck">Whether the deck contains the card in this quantity, in the commander, companion or main zone.</param>
public sealed record DeckComboCard(Guid OracleId, string Name, int Quantity, bool InDeck);
