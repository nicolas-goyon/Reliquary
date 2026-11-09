using System.Globalization;
using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The reason codes of a refused action. The hub sends them in <c>{ rejected: code }</c>.
/// </summary>
public static class ActionRejections
{
    /// <summary>The action is not valid: unknown type, missing field, or a value out of range.</summary>
    public const string InvalidAction = "invalid_action";

    /// <summary>The action changes the objects of another player. In an online game, each player changes only their own objects.</summary>
    public const string NotYourObject = "not_your_object";

    /// <summary>Only the active player can end the turn.</summary>
    public const string NotYourTurn = "not_your_turn";

    /// <summary>The card is not a card of the player.</summary>
    public const string UnknownCard = "unknown_card";

    /// <summary>The game did not start.</summary>
    public const string NotStarted = "not_started";

    /// <summary>The game ended.</summary>
    public const string GameClosed = "game_closed";

    /// <summary>The client sent too many actions in a short time.</summary>
    public const string RateLimited = "rate_limited";
}

/// <summary>
/// Checks the actions of an online game before the table engine applies them, and gives the public text of each action.
/// The table engine has no game rules: these checks only keep each player on their own objects. See docs/architecture/online-games.md.
/// </summary>
internal static class ActionRules
{
    /// <summary>The maximum number of cards of one draw.</summary>
    public const int MaxCount = 20;

    /// <summary>The maximum length of the name of a token.</summary>
    public const int MaxTokenName = 64;

    /// <summary>The maximum change of a number in one action, for example the life points.</summary>
    public const int MaxChange = 1000;

    /// <summary>
    /// Checks an action of a player.
    /// </summary>
    /// <param name="table">The table before the action.</param>
    /// <param name="playerId">The player of the seat that sends the action.</param>
    /// <param name="action">The action.</param>
    /// <returns>The reason code of the refusal, or <see langword="null"/> when the action can apply.</returns>
    public static string? Check(TableState table, string playerId, TableAction action)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(action);
        if (action is NextTurnAction)
        {
            return table.ActivePlayerId == playerId ? null : ActionRejections.NotYourTurn;
        }

        var (owner, cardId) = Target(action);
        if (owner != playerId)
        {
            return ActionRejections.NotYourObject;
        }

        var player = table.Players.FirstOrDefault(item => item.Id == playerId);
        if (player is null)
        {
            return ActionRejections.NotYourObject;
        }

        if (cardId is not null && TableEngine.ZoneOf(player, cardId) is null)
        {
            return ActionRejections.UnknownCard;
        }

        return action switch
        {
            DrawAction draw when draw.Count is < 1 or > MaxCount => ActionRejections.InvalidAction,
            TokenAction token when string.IsNullOrWhiteSpace(token.Name) || token.Name.Length > MaxTokenName || token.Name.Any(char.IsControl) => ActionRejections.InvalidAction,
            CounterAction counter when Math.Abs(counter.Change) > MaxChange => ActionRejections.InvalidAction,
            LifeAction life when Math.Abs(life.Change) > MaxChange => ActionRejections.InvalidAction,
            CommanderDamageAction damage when Math.Abs(damage.Change) > MaxChange || table.Players.All(item => item.Id != damage.FromPlayerId) => ActionRejections.InvalidAction,
            PlayerCounterAction counter when Math.Abs(counter.Change) > MaxChange || !Enum.IsDefined(counter.Counter) => ActionRejections.InvalidAction,
            MoveAction move when !Enum.IsDefined(move.To) => ActionRejections.InvalidAction,
            _ => null,
        };
    }

    /// <summary>
    /// Gives the public text of an action. The text never gives the name of a card in a hidden zone.
    /// </summary>
    /// <param name="nickname">The name of the player.</param>
    /// <param name="before">The table before the action.</param>
    /// <param name="action">The action.</param>
    /// <returns>The text, for example "Ana drew 2 cards".</returns>
    public static string Describe(string nickname, TableState before, TableAction action)
    {
        ArgumentNullException.ThrowIfNull(before);
        var player = before.Players.FirstOrDefault(item => action is not NextTurnAction && item.Id == Target(action).Owner);
        string Card(string cardId)
        {
            // A card is public on the battlefield, in the graveyard, in the exile and in the command zone, when it is face up.
            var zone = player is null ? null : TableEngine.ZoneOf(player, cardId);
            var card = zone is null ? null : player!.Zones.Get(zone.Value).FirstOrDefault(item => item.Id == cardId);
            return card is null || card.FaceDown || zone is TableZone.Library or TableZone.Hand ? "a card" : card.Name;
        }

        string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count.ToString(CultureInfo.InvariantCulture)} {noun}s";

        return action switch
        {
            DrawAction draw => $"{nickname} drew {Count(draw.Count ?? 1, "card")}.",
            ShuffleAction => $"{nickname} shuffled the library.",
            MulliganAction => $"{nickname} took a mulligan.",
            BottomAction => $"{nickname} put a card at the bottom of the library.",
            MoveAction move => $"{nickname} moved {Card(move.CardId)} to the {Zone(move.To)}.",
            TapAction tap => $"{nickname} tapped or untapped {Card(tap.CardId)}.",
            UntapAllAction => $"{nickname} untapped all their permanents.",
            FlipAction => $"{nickname} turned a card.",
            CounterAction counter => $"{nickname} changed the counters on {Card(counter.CardId)} by {Signed(counter.Change)}.",
            TokenAction token => $"{nickname} created the token {token.Name}.",
            LifeAction life => $"{nickname} changed their life by {Signed(life.Change)}.",
            CommanderDamageAction damage => $"{nickname} changed the commander damage by {Signed(damage.Change)}.",
            PlayerCounterAction counter => $"{nickname} changed their {Zone(counter.Counter)} counters by {Signed(counter.Change)}.",
            NextTurnAction => $"{nickname} ended the turn.",
            _ => $"{nickname} did an action.",
        };
    }

    private static (string Owner, string? CardId) Target(TableAction action) => action switch
    {
        DrawAction draw => (draw.PlayerId, null),
        ShuffleAction shuffle => (shuffle.PlayerId, null),
        MulliganAction mulligan => (mulligan.PlayerId, null),
        BottomAction bottom => (bottom.PlayerId, bottom.CardId),
        MoveAction move => (move.PlayerId, move.CardId),
        TapAction tap => (tap.PlayerId, tap.CardId),
        UntapAllAction untap => (untap.PlayerId, null),
        FlipAction flip => (flip.PlayerId, flip.CardId),
        CounterAction counter => (counter.PlayerId, counter.CardId),
        TokenAction token => (token.PlayerId, null),
        LifeAction life => (life.PlayerId, null),
        CommanderDamageAction damage => (damage.PlayerId, null),
        PlayerCounterAction counter => (counter.PlayerId, null),
        _ => (string.Empty, null),
    };

    private static string Zone<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Signed(int value) => value > 0 ? $"+{value.ToString(CultureInfo.InvariantCulture)}" : value.ToString(CultureInfo.InvariantCulture);
}
