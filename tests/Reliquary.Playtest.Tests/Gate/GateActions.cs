using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Tests.Gate;

/// <summary>
/// Makes random actions that are legal for a player, from the view of this player only, as a person at the table.
/// The weights are near the actions of a real game: many moves, taps and draws, few shuffles and mulligans.
/// </summary>
internal static class GateActions
{
    /// <summary>The types of all the actions of engine.ts. The gate test sends each type.</summary>
    public static IReadOnlyList<string> AllTypes { get; } =
        ["draw", "shuffle", "mulligan", "bottom", "move", "tap", "untapAll", "flip", "counter", "token", "life", "commanderDamage", "playerCounter", "nextTurn"];

    private static readonly string[] TokenNames = ["Goblin", "Soldier", "Treasure", "Elf Warrior", "Zombie Army"];

    private static readonly TableZone[] Targets = [TableZone.Library, TableZone.Hand, TableZone.Battlefield, TableZone.Graveyard, TableZone.Exile, TableZone.Command];

    /// <summary>
    /// Makes the next action of a player.
    /// </summary>
    /// <param name="random">The random generator of the player.</param>
    /// <param name="view">The view of the player: the player sees the cards of the own hand, but not the cards of the own library.</param>
    /// <param name="seat">The ID of the player.</param>
    /// <returns>The action.</returns>
    public static TableAction Next(Random random, TableState view, string seat)
    {
        var me = view.Players.Single(player => player.Id == seat);
        var zones = me.Zones;

        // After a mulligan, the player must put cards at the bottom first.
        if (me.CardsToBottom > 0 && zones.Hand.Count > 0)
        {
            return new BottomAction(seat, Pick(random, zones.Hand).Id);
        }

        // The cards that the player can see and choose. The library is hidden, also for its owner.
        var known = zones.Hand.Concat(zones.Battlefield).Concat(zones.Graveyard).Concat(zones.Exile).Concat(zones.Command).ToList();
        var battlefield = zones.Battlefield;
        var others = view.Players.Where(player => player.Id != seat).Select(player => player.Id).ToList();

        var choices = new List<(int Weight, Func<TableAction> Make)>
        {
            (14, () => new MoveAction(seat, Pick(random, known).Id, Pick(random, Targets), random.Next(3) switch { 0 => CardPosition.Top, 1 => CardPosition.Bottom, _ => null })),
            (8, () => new LifeAction(seat, random.Next(2) == 0 ? -random.Next(1, 6) : random.Next(1, 4))),
            (2, () => new UntapAllAction(seat)),
            (3, () => new TokenAction(seat, Pick(random, TokenNames))),
            (2, () => new ShuffleAction(seat)),
            (1, () => new MulliganAction(seat)),
            (3, () => new PlayerCounterAction(seat, (PlayerCounter)random.Next(3), random.Next(2) == 0 ? 1 : -1)),
        };

        if (zones.Library.Count > 0)
        {
            choices.Add((10, () => new DrawAction(seat, random.Next(4) == 0 ? random.Next(2, 4) : null)));
        }

        if (battlefield.Count > 0)
        {
            choices.Add((12, () => new TapAction(seat, Pick(random, battlefield).Id)));
            choices.Add((4, () => new FlipAction(seat, Pick(random, battlefield).Id)));
            choices.Add((6, () => new CounterAction(seat, Pick(random, battlefield).Id, random.Next(2) == 0 ? random.Next(1, 4) : -1)));
        }

        if (zones.Hand.Count > 0)
        {
            // Play a card from the hand: the most frequent action of a real game.
            choices.Add((10, () => new MoveAction(seat, Pick(random, zones.Hand).Id, TableZone.Battlefield)));
        }

        if (view.Commander && others.Count > 0)
        {
            choices.Add((2, () => new CommanderDamageAction(seat, Pick(random, others), random.Next(1, 8))));
        }

        if (view.ActivePlayerId == seat)
        {
            choices.Add((8, () => new NextTurnAction()));
        }

        if (known.Count == 0)
        {
            choices.RemoveAt(0);
        }

        var total = choices.Sum(choice => choice.Weight);
        var roll = random.Next(total);
        foreach (var (weight, make) in choices)
        {
            if (roll < weight)
            {
                return make();
            }

            roll -= weight;
        }

        throw new InvalidOperationException("The weights of the actions are not correct.");
    }

    /// <summary>Gets the type of an action, as the JSON property <c>type</c>.</summary>
    public static string TypeOf(TableAction action) =>
        System.Text.Json.JsonSerializer.SerializeToNode(action, TableJson.Options)!["type"]!.GetValue<string>();

    private static T Pick<T>(Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];
}
