using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reliquary.Playtest.Engine;

// The types of this file mirror the types of web/src/playtest/engine.ts, with the same JSON.
// Keep all the state types in this file. When engine.ts changes, change this file and its tests.
// The C# engine port (issue #227) uses these types.

/// <summary>
/// The zones of a player on the table. The JSON names are the names of <c>TableZone</c> in engine.ts.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TableZone>))]
public enum TableZone
{
    /// <summary>The library.</summary>
    [JsonStringEnumMemberName("library")]
    Library,

    /// <summary>The hand.</summary>
    [JsonStringEnumMemberName("hand")]
    Hand,

    /// <summary>The battlefield.</summary>
    [JsonStringEnumMemberName("battlefield")]
    Battlefield,

    /// <summary>The graveyard.</summary>
    [JsonStringEnumMemberName("graveyard")]
    Graveyard,

    /// <summary>The exile.</summary>
    [JsonStringEnumMemberName("exile")]
    Exile,

    /// <summary>The command zone.</summary>
    [JsonStringEnumMemberName("command")]
    Command,
}

/// <summary>
/// The counters of a player. The players enter them by hand: the engine has no game rules.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlayerCounter>))]
public enum PlayerCounter
{
    /// <summary>Poison counters.</summary>
    [JsonStringEnumMemberName("poison")]
    Poison,

    /// <summary>Energy counters.</summary>
    [JsonStringEnumMemberName("energy")]
    Energy,

    /// <summary>Experience counters.</summary>
    [JsonStringEnumMemberName("experience")]
    Experience,
}

/// <summary>
/// The position of a card that goes into a zone.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CardPosition>))]
public enum CardPosition
{
    /// <summary>The top of the zone.</summary>
    [JsonStringEnumMemberName("top")]
    Top,

    /// <summary>The bottom of the zone.</summary>
    [JsonStringEnumMemberName("bottom")]
    Bottom,
}

/// <summary>
/// One copy of a card on the table.
/// </summary>
/// <param name="Id">The ID of this copy on the table.</param>
/// <param name="OracleId">The Oracle card, or an empty text for a token.</param>
/// <param name="Name">The name of the card.</param>
/// <param name="ImageUri">The address of the image, or <see langword="null"/>.</param>
/// <param name="IsLand">Whether the card is a land.</param>
/// <param name="ManaValue">The mana value.</param>
/// <param name="IsToken">Whether the card is a token.</param>
/// <param name="Tapped">Whether the card is tapped.</param>
/// <param name="FaceDown">Whether the card shows its back.</param>
/// <param name="Counters">The number of counters on the card.</param>
public sealed record TableCard(
    string Id,
    string OracleId,
    string Name,
    string? ImageUri,
    bool IsLand,
    double ManaValue,
    bool IsToken,
    bool Tapped,
    bool FaceDown,
    int Counters);

/// <summary>
/// The cards of each zone of a player. The JSON is the object <c>Record&lt;TableZone, TableCard[]&gt;</c> of engine.ts.
/// </summary>
public sealed record TableZones(
    IReadOnlyList<TableCard> Library,
    IReadOnlyList<TableCard> Hand,
    IReadOnlyList<TableCard> Battlefield,
    IReadOnlyList<TableCard> Graveyard,
    IReadOnlyList<TableCard> Exile,
    IReadOnlyList<TableCard> Command)
{
    /// <summary>Gets zones with no card.</summary>
    public static TableZones Empty { get; } = new([], [], [], [], [], []);

    /// <summary>
    /// Gets the cards of a zone.
    /// </summary>
    /// <param name="zone">The zone.</param>
    /// <returns>The cards, from the top of the zone.</returns>
    public IReadOnlyList<TableCard> Get(TableZone zone) => zone switch
    {
        TableZone.Library => Library,
        TableZone.Hand => Hand,
        TableZone.Battlefield => Battlefield,
        TableZone.Graveyard => Graveyard,
        TableZone.Exile => Exile,
        TableZone.Command => Command,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "The zone is not a table zone."),
    };

    /// <summary>
    /// Gives a copy of the zones with new cards in one zone.
    /// </summary>
    /// <param name="zone">The zone to change.</param>
    /// <param name="cards">The new cards of the zone.</param>
    /// <returns>The new zones.</returns>
    public TableZones With(TableZone zone, IReadOnlyList<TableCard> cards) => zone switch
    {
        TableZone.Library => this with { Library = cards },
        TableZone.Hand => this with { Hand = cards },
        TableZone.Battlefield => this with { Battlefield = cards },
        TableZone.Graveyard => this with { Graveyard = cards },
        TableZone.Exile => this with { Exile = cards },
        TableZone.Command => this with { Command = cards },
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "The zone is not a table zone."),
    };
}

/// <summary>
/// The counters of a player. The JSON is the object <c>Record&lt;PlayerCounter, number&gt;</c> of engine.ts.
/// </summary>
public sealed record PlayerCounters(int Poison, int Energy, int Experience)
{
    /// <summary>Gets counters with the value 0.</summary>
    public static PlayerCounters None { get; } = new(0, 0, 0);
}

/// <summary>
/// One player on the table.
/// </summary>
/// <param name="Id">The ID of the player.</param>
/// <param name="Name">The name of the player.</param>
/// <param name="Life">The life points.</param>
/// <param name="CommanderDamage">The commander damage that this player took, by the ID of the other player.</param>
/// <param name="Counters">The counters of the player.</param>
/// <param name="Mulligans">The number of mulligans.</param>
/// <param name="CardsToBottom">The number of cards that the player must put at the bottom of the library after a mulligan.</param>
/// <param name="Zones">The cards of each zone.</param>
public sealed record TablePlayer(
    string Id,
    string Name,
    int Life,
    IReadOnlyDictionary<string, int> CommanderDamage,
    PlayerCounters Counters,
    int Mulligans,
    int CardsToBottom,
    TableZones Zones);

/// <summary>
/// The state of a table.
/// </summary>
/// <param name="Players">The players, in the order of the turns.</param>
/// <param name="ActivePlayerId">The ID of the active player.</param>
/// <param name="Turn">The number of the turn. The first turn has the number 1.</param>
/// <param name="Seed">
/// The state of the random number generator. engine.ts keeps a 32-bit integer: unsigned before the first shuffle, signed after it.
/// A <see langword="long"/> keeps the two ranges.
/// </param>
/// <param name="NextTokenId">The next number of the IDs of the tokens.</param>
public sealed record TableState(
    IReadOnlyList<TablePlayer> Players,
    string ActivePlayerId,
    int Turn,
    long Seed,
    int NextTokenId);

/// <summary>
/// One card of the setup of a player: a card of the table with no table fields.
/// </summary>
/// <param name="Id">The ID of this copy on the table.</param>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Name">The name of the card.</param>
/// <param name="ImageUri">The address of the image, or <see langword="null"/>.</param>
/// <param name="IsLand">Whether the card is a land.</param>
/// <param name="ManaValue">The mana value.</param>
public sealed record SetupCard(string Id, string OracleId, string Name, string? ImageUri, bool IsLand, double ManaValue);

/// <summary>
/// The cards of a player at the start of a game.
/// </summary>
/// <param name="Id">The ID of the player.</param>
/// <param name="Name">The name of the player.</param>
/// <param name="Library">The cards of the library, with no order.</param>
/// <param name="Command">The cards that start in the command zone.</param>
public sealed record PlayerSetup(string Id, string Name, IReadOnlyList<SetupCard> Library, IReadOnlyList<SetupCard> Command);

/// <summary>
/// An action of the table engine. The JSON property <c>type</c> gives the kind of action, as the union <c>TableAction</c> of engine.ts.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(DrawAction), "draw")]
[JsonDerivedType(typeof(ShuffleAction), "shuffle")]
[JsonDerivedType(typeof(MulliganAction), "mulligan")]
[JsonDerivedType(typeof(BottomAction), "bottom")]
[JsonDerivedType(typeof(MoveAction), "move")]
[JsonDerivedType(typeof(TapAction), "tap")]
[JsonDerivedType(typeof(UntapAllAction), "untapAll")]
[JsonDerivedType(typeof(FlipAction), "flip")]
[JsonDerivedType(typeof(CounterAction), "counter")]
[JsonDerivedType(typeof(TokenAction), "token")]
[JsonDerivedType(typeof(LifeAction), "life")]
[JsonDerivedType(typeof(CommanderDamageAction), "commanderDamage")]
[JsonDerivedType(typeof(PlayerCounterAction), "playerCounter")]
[JsonDerivedType(typeof(NextTurnAction), "nextTurn")]
public abstract record TableAction;

/// <summary>The player draws cards. With no count, the player draws 1 card.</summary>
public sealed record DrawAction(string PlayerId, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count = null) : TableAction;

/// <summary>The player shuffles the library.</summary>
public sealed record ShuffleAction(string PlayerId) : TableAction;

/// <summary>The player takes a London mulligan.</summary>
public sealed record MulliganAction(string PlayerId) : TableAction;

/// <summary>After a mulligan, the player puts a card of the hand at the bottom of the library.</summary>
public sealed record BottomAction(string PlayerId, string CardId) : TableAction;

/// <summary>The player moves a card to a zone. With no position, a card goes to the top of the library or to the bottom of another zone.</summary>
public sealed record MoveAction(
    string PlayerId,
    string CardId,
    TableZone To,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CardPosition? Position = null) : TableAction;

/// <summary>The player taps or untaps a card.</summary>
public sealed record TapAction(string PlayerId, string CardId) : TableAction;

/// <summary>The player untaps all the cards of the battlefield.</summary>
public sealed record UntapAllAction(string PlayerId) : TableAction;

/// <summary>The player turns a card face down or face up.</summary>
public sealed record FlipAction(string PlayerId, string CardId) : TableAction;

/// <summary>The player adds counters to a card, or removes counters from it.</summary>
public sealed record CounterAction(string PlayerId, string CardId, int Change) : TableAction;

/// <summary>The player puts a token on the battlefield.</summary>
public sealed record TokenAction(string PlayerId, string Name) : TableAction;

/// <summary>The life points of the player change.</summary>
public sealed record LifeAction(string PlayerId, int Change) : TableAction;

/// <summary>The commander damage that the player took from another player changes.</summary>
public sealed record CommanderDamageAction(string PlayerId, string FromPlayerId, int Change) : TableAction;

/// <summary>A counter of the player changes.</summary>
public sealed record PlayerCounterAction(string PlayerId, PlayerCounter Counter, int Change) : TableAction;

/// <summary>The next player starts a turn.</summary>
public sealed record NextTurnAction : TableAction;

/// <summary>
/// The JSON settings of the table types: the property names of engine.ts.
/// </summary>
public static class TableJson
{
    /// <summary>Gets the settings.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        // A client can send "type" after the other properties.
        AllowOutOfOrderMetadataProperties = true,
    };
}
