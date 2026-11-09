namespace Reliquary.Playtest.Engine;

/// <summary>
/// The table engine of online games: the C# port of web/src/playtest/engine.ts.
/// The engine has no game rules. It is a pure function: the same state and the same action always give the same new state.
/// It uses no clock, no input or output and no random source other than the seed of the state.
/// </summary>
/// <remarks>
/// The two engines must stay equal. The conformance vectors in tests/vectors/table-engine record the states of engine.ts.
/// When engine.ts changes, make the same change here and generate the vectors again ("npm run vectors" in web/).
/// See docs/decisions/0047-trusted-server-for-online-games.md.
/// </remarks>
public static class TableEngine
{
    /// <summary>The number of cards of an opening hand.</summary>
    public const int OpeningHandSize = 7;

    /// <summary>
    /// Starts a table: each library has a random order, and each player has an opening hand.
    /// </summary>
    /// <param name="setups">The players, in the order of the turns.</param>
    /// <param name="seed">The seed of the random number generator. engine.ts gets a value from 0 to 2^32 - 1.</param>
    /// <param name="startingLife">The life points of each player at the start.</param>
    /// <param name="commander">Whether the game uses the commander damage.</param>
    /// <returns>The new table.</returns>
    public static TableState CreateTable(IReadOnlyList<PlayerSetup> setups, long seed, int startingLife, bool commander = false)
    {
        ArgumentNullException.ThrowIfNull(setups);
        var current = seed;
        var players = new List<TablePlayer>(setups.Count);
        foreach (var setup in setups)
        {
            var library = Shuffled(setup.Library.Select(FreshCard).ToList(), current);
            current = library.Seed;
            var zones = TableZones.Empty with
            {
                Library = Slice(library.Items, OpeningHandSize),
                Hand = Slice(library.Items, 0, OpeningHandSize),
                Command = setup.Command.Select(FreshCard).ToList(),
            };
            players.Add(new TablePlayer(setup.Id, setup.Name, startingLife, new Dictionary<string, int>(), PlayerCounters.None, 0, 0, zones));
        }

        return new TableState(players, commander, players.Count > 0 ? players[0].Id : string.Empty, 1, current, 1);
    }

    /// <summary>
    /// Applies one action. An action on a card or a player that does not exist does not change the state.
    /// </summary>
    /// <param name="state">The state of the table.</param>
    /// <param name="action">The action.</param>
    /// <returns>The new state.</returns>
    public static TableState Apply(TableState state, TableAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        return action switch
        {
            DrawAction draw => UpdatePlayer(state, draw.PlayerId, player => DrawCards(player, draw.Count ?? 1)),
            ShuffleAction shuffle => Shuffle(state, shuffle.PlayerId),
            MulliganAction mulligan => Mulligan(state, mulligan.PlayerId),
            BottomAction bottom => UpdatePlayer(state, bottom.PlayerId, player =>
                player.CardsToBottom == 0 || ZoneOf(player, bottom.CardId) != TableZone.Hand
                    ? player
                    : MoveCard(player, bottom.CardId, TableZone.Library, CardPosition.Bottom) with { CardsToBottom = player.CardsToBottom - 1 }),
            MoveAction move => UpdatePlayer(state, move.PlayerId, player =>
                MoveCard(player, move.CardId, move.To, move.Position ?? (move.To == TableZone.Library ? CardPosition.Top : CardPosition.Bottom))),
            TapAction tap => UpdatePlayer(state, tap.PlayerId, player => UpdateCard(player, tap.CardId, card => card with { Tapped = !card.Tapped })),
            UntapAllAction untap => UpdatePlayer(state, untap.PlayerId, player =>
                player with { Zones = player.Zones with { Battlefield = player.Zones.Battlefield.Select(card => card with { Tapped = false }).ToList() } }),
            FlipAction flip => UpdatePlayer(state, flip.PlayerId, player => UpdateCard(player, flip.CardId, card => card with { FaceDown = !card.FaceDown })),
            CounterAction counter => UpdatePlayer(state, counter.PlayerId, player =>
                UpdateCard(player, counter.CardId, card => card with { Counters = Math.Max(0, card.Counters + counter.Change) })),
            TokenAction token => Token(state, token),
            LifeAction life => UpdatePlayer(state, life.PlayerId, player => player with { Life = player.Life + life.Change }),
            CommanderDamageAction damage => CommanderDamage(state, damage),
            PlayerCounterAction counter => UpdatePlayer(state, counter.PlayerId, player => player with { Counters = ChangeCounter(player.Counters, counter) }),
            NextTurnAction => NextTurn(state),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "The action is not a table action."),
        };
    }

    /// <summary>
    /// Gets the zone of a card, or <see langword="null"/> when the player does not have the card.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="cardId">The ID of the card.</param>
    /// <returns>The zone, or <see langword="null"/>.</returns>
    public static TableZone? ZoneOf(TablePlayer player, string cardId)
    {
        ArgumentNullException.ThrowIfNull(player);
        foreach (var zone in Zones)
        {
            if (player.Zones.Get(zone).Any(card => card.Id == cardId))
            {
                return zone;
            }
        }

        return null;
    }

    /// <summary>The zones in the order of <c>tableZones</c> in engine.ts.</summary>
    private static readonly TableZone[] Zones =
        [TableZone.Library, TableZone.Hand, TableZone.Battlefield, TableZone.Graveyard, TableZone.Exile, TableZone.Command];

    /// <summary>
    /// The mulberry32 generator, with the 32-bit integer arithmetic of JavaScript.
    /// </summary>
    /// <param name="seed">The state of the generator: a signed or an unsigned 32-bit value.</param>
    /// <returns>A number from 0 (included) to 1 (not included), and the new state. The new state is always a signed 32-bit value.</returns>
    internal static (double Value, long Seed) NextRandom(long seed)
    {
        unchecked
        {
            // (seed + 0x6d2b79f5) | 0: the low 32 bits of the sum, as a signed value.
            var next = (int)(seed + 0x6d2b79f5L);
            var mixed = (int)((uint)next ^ ((uint)next >> 15)) * (1 | next);
            mixed = (mixed + ((int)((uint)mixed ^ ((uint)mixed >> 7)) * (61 | mixed))) ^ mixed;
            var value = (uint)mixed ^ ((uint)mixed >> 14);
            return (value / 4294967296.0, next);
        }
    }

    /// <summary>The Fisher-Yates shuffle of engine.ts.</summary>
    internal static (List<T> Items, long Seed) Shuffled<T>(IReadOnlyList<T> items, long seed)
    {
        var result = items.ToList();
        var current = seed;
        for (var index = result.Count - 1; index > 0; index--)
        {
            var random = NextRandom(current);
            current = random.Seed;
            var other = (int)Math.Floor(random.Value * (index + 1));
            (result[index], result[other]) = (result[other], result[index]);
        }

        return (result, current);
    }

    private static TableCard FreshCard(SetupCard card) =>
        new(card.Id, card.OracleId, card.Name, card.ImageUri, card.IsLand, card.ManaValue, false, false, false, 0);

    /// <summary>The <c>Array.prototype.slice</c> of JavaScript: a negative index counts from the end.</summary>
    private static List<T> Slice<T>(IReadOnlyList<T> items, int start, int? end = null)
    {
        static int Clamp(int index, int count) => index < 0 ? Math.Max(count + index, 0) : Math.Min(index, count);
        var from = Clamp(start, items.Count);
        var to = Clamp(end ?? items.Count, items.Count);
        return from >= to ? [] : items.Skip(from).Take(to - from).ToList();
    }

    private static TableState UpdatePlayer(TableState state, string playerId, Func<TablePlayer, TablePlayer> update) =>
        state with { Players = state.Players.Select(player => player.Id == playerId ? update(player) : player).ToList() };

    private static TablePlayer UpdateCard(TablePlayer player, string cardId, Func<TableCard, TableCard> update)
    {
        var zones = player.Zones;
        foreach (var zone in Zones)
        {
            var cards = zones.Get(zone);
            if (cards.Any(card => card.Id == cardId))
            {
                zones = zones.With(zone, cards.Select(card => card.Id == cardId ? update(card) : card).ToList());
            }
        }

        return player with { Zones = zones };
    }

    private static TablePlayer MoveCard(TablePlayer player, string cardId, TableZone to, CardPosition position)
    {
        var from = ZoneOf(player, cardId);
        if (from is not { } source)
        {
            return player;
        }

        var card = player.Zones.Get(source).First(item => item.Id == cardId);
        var without = player.Zones.With(source, player.Zones.Get(source).Where(item => item.Id != cardId).ToList());

        // A token stops to exist when it leaves the battlefield.
        if (card.IsToken && to != TableZone.Battlefield)
        {
            return player with { Zones = without };
        }

        var moved = to == TableZone.Battlefield ? card : card with { Tapped = false, FaceDown = false, Counters = 0 };
        List<TableCard> target = position == CardPosition.Top ? [moved, .. without.Get(to)] : [.. without.Get(to), moved];
        return player with { Zones = without.With(to, target) };
    }

    private static TablePlayer DrawCards(TablePlayer player, int count)
    {
        var library = player.Zones.Library;
        var drawn = Slice(library, 0, count);
        return player with
        {
            Zones = player.Zones with { Library = Slice(library, drawn.Count), Hand = [.. player.Zones.Hand, .. drawn] },
        };
    }

    private static TableState Shuffle(TableState state, string playerId)
    {
        var player = state.Players.FirstOrDefault(item => item.Id == playerId);
        if (player is null)
        {
            return state;
        }

        var library = Shuffled(player.Zones.Library, state.Seed);
        return UpdatePlayer(state, playerId, item => item with { Zones = item.Zones with { Library = library.Items } }) with { Seed = library.Seed };
    }

    /// <summary>
    /// The London mulligan: the hand goes back, the library gets a shuffle, and the player draws 7 cards.
    /// The player then puts one card at the bottom for each mulligan.
    /// </summary>
    private static TableState Mulligan(TableState state, string playerId)
    {
        var player = state.Players.FirstOrDefault(item => item.Id == playerId);
        if (player is null)
        {
            return state;
        }

        var library = Shuffled([.. player.Zones.Library, .. player.Zones.Hand], state.Seed);
        var mulligans = player.Mulligans + 1;
        var returned = player with { Zones = player.Zones with { Library = library.Items, Hand = [] } };
        var next = DrawCards(returned, OpeningHandSize) with { Mulligans = mulligans, CardsToBottom = Math.Min(mulligans, OpeningHandSize) };
        return UpdatePlayer(state, playerId, _ => next) with { Seed = library.Seed };
    }

    private static TableState Token(TableState state, TokenAction action)
    {
        var token = new TableCard($"token-{state.NextTokenId}", string.Empty, action.Name, null, false, 0, true, false, false, 0);
        var next = UpdatePlayer(state, action.PlayerId, player =>
            player with { Zones = player.Zones with { Battlefield = [.. player.Zones.Battlefield, token] } });
        return next with { NextTokenId = state.NextTokenId + 1 };
    }

    private static TableState CommanderDamage(TableState state, CommanderDamageAction action)
    {
        if (!state.Commander)
        {
            return state;
        }

        return UpdatePlayer(state, action.PlayerId, player =>
        {
            // A copy keeps the order of the keys, as the object spread of engine.ts.
            var damage = new Dictionary<string, int>(player.CommanderDamage);
            damage[action.FromPlayerId] = Math.Max(0, damage.GetValueOrDefault(action.FromPlayerId) + action.Change);
            return player with { CommanderDamage = damage };
        });
    }

    private static PlayerCounters ChangeCounter(PlayerCounters counters, PlayerCounterAction action) => action.Counter switch
    {
        PlayerCounter.Poison => counters with { Poison = Math.Max(0, counters.Poison + action.Change) },
        PlayerCounter.Energy => counters with { Energy = Math.Max(0, counters.Energy + action.Change) },
        PlayerCounter.Experience => counters with { Experience = Math.Max(0, counters.Experience + action.Change) },
        _ => throw new ArgumentOutOfRangeException(nameof(action), action.Counter, "The counter is not a player counter."),
    };

    private static TableState NextTurn(TableState state)
    {
        if (state.Players.Count == 0)
        {
            return state;
        }

        // findIndex gives -1 for an unknown active player, so the first player gets the turn.
        var index = state.Players.Select(player => player.Id).ToList().IndexOf(state.ActivePlayerId);
        var next = state.Players[(index + 1) % state.Players.Count];
        var untapped = Apply(state, new UntapAllAction(next.Id));
        return Apply(untapped with { ActivePlayerId = next.Id, Turn = state.Turn + 1 }, new DrawAction(next.Id));
    }
}
