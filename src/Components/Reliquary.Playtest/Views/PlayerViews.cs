using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Views;

/// <summary>
/// The view of one player in an online game, the view hash and the zone patches: the C# side of
/// web/src/playtest/views.ts and web/src/playtest/projection.ts. ADR 0047 and issue #228 give the design.
/// The shared vectors in tests/vectors/views keep the two sides equal.
/// </summary>
public static class PlayerViews
{
    /// <summary>The viewer ID of a viewer that is not a player. This viewer sees only public information.</summary>
    public const string PublicViewerId = "table";

    /// <summary>The fields of a player that a patch can change, in the order of <c>playerFields</c> in views.ts.</summary>
    public static IReadOnlyList<string> PlayerFields { get; } = ["name", "life", "commanderDamage", "counters", "mulligans", "cardsToBottom"];

    /// <summary>The fields of the table that a patch can change, in the order of <c>tableFields</c> in views.ts.</summary>
    public static IReadOnlyList<string> TableFields { get; } = ["activePlayerId", "turn", "commander", "seed", "nextTokenId"];

    /// <summary>The zones in the order of <c>tableZones</c> in engine.ts.</summary>
    private static readonly TableZone[] Zones =
        [TableZone.Library, TableZone.Hand, TableZone.Battlefield, TableZone.Graveyard, TableZone.Exile, TableZone.Command];

    /// <summary>
    /// Gives the state that a viewer can see. It is a pure function.
    /// <list type="bullet">
    /// <item>Nobody sees the cards of a library. The owner also does not know the order of the library.</item>
    /// <item>Only the owner sees the cards of the hand and the face-down cards. All the other cards are public.</item>
    /// <item>A hidden card has no identity: no Oracle ID, no name and no image. Its ID tells only its player, its zone and its position.</item>
    /// <item>The view has no seed, because the seed gives the order of each library after a shuffle.</item>
    /// </list>
    /// </summary>
    /// <param name="state">The full state of the table.</param>
    /// <param name="viewerId">The ID of a player, or <see cref="PublicViewerId"/>.</param>
    /// <returns>The view.</returns>
    public static TableState View(TableState state, string viewerId)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { Seed = 0, Players = state.Players.Select(player => ViewOfPlayer(player, viewerId)).ToList() };
    }

    /// <summary>
    /// Gives the JSON of a view (or of a state), with the property names of engine.ts.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <returns>The JSON object.</returns>
    public static JsonObject ToJson(TableState view) => JsonSerializer.SerializeToNode(view, TableJson.Options)!.AsObject();

    /// <summary>
    /// Gives the hash of a view: the 64-bit FNV-1a hash of its canonical JSON, as 16 lowercase hexadecimal digits.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <returns>The hash.</returns>
    public static string Hash(TableState view) => CanonicalJson.Fnv1a64(CanonicalJson.Write(ToJson(view)));

    /// <summary>
    /// Gives the changes from one view to the next view of the same viewer.
    /// The changes come in a fixed order: the table fields, then each player in the order of the view, with its fields and then its zones.
    /// </summary>
    /// <param name="before">The view before.</param>
    /// <param name="after">The view after.</param>
    /// <returns>The changes of the patch. No change when the two views are equal.</returns>
    /// <exception cref="ArgumentException">The two views do not have the same players in the same order. Send a full view.</exception>
    public static IReadOnlyList<ViewChange> Diff(TableState before, TableState after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Players.Count != after.Players.Count || before.Players.Where((player, index) => player.Id != after.Players[index].Id).Any())
        {
            throw new ArgumentException("The two views do not have the same players. Send a full view.", nameof(after));
        }

        var old = ToJson(before);
        var next = ToJson(after);
        var changes = new List<ViewChange>();
        foreach (var field in TableFields)
        {
            if (!Same(old[field], next[field]))
            {
                changes.Add(new TableFieldChange(field, next[field]?.DeepClone()));
            }
        }

        var oldPlayers = old["players"]!.AsArray();
        var nextPlayers = next["players"]!.AsArray();
        for (var index = 0; index < after.Players.Count; index++)
        {
            var seat = after.Players[index].Id;
            var oldPlayer = oldPlayers[index]!.AsObject();
            var nextPlayer = nextPlayers[index]!.AsObject();
            foreach (var field in PlayerFields)
            {
                if (!Same(oldPlayer[field], nextPlayer[field]))
                {
                    changes.Add(new PlayerFieldChange(seat, field, nextPlayer[field]?.DeepClone()));
                }
            }

            foreach (var zone in Zones)
            {
                var cards = after.Players[index].Zones.Get(zone);
                if (!Same(ZoneJson(before.Players[index], zone), ZoneJson(after.Players[index], zone)))
                {
                    changes.Add(new ZoneChange(seat, zone, cards));
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// Applies the changes of a patch to a view. It is a pure function: the view does not change.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <param name="changes">The changes of the patch.</param>
    /// <returns>The new view.</returns>
    /// <exception cref="ArgumentException">A change is for a seat that is not in the view, or for an unknown field. Ask for a full view.</exception>
    public static TableState Apply(TableState view, IEnumerable<ViewChange> changes)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(changes);
        var json = ToJson(view);
        var players = json["players"]!.AsArray();
        foreach (var change in changes)
        {
            switch (change)
            {
                case TableFieldChange table when TableFields.Contains(table.Field):
                    json[table.Field] = table.Value?.DeepClone();
                    break;
                case PlayerFieldChange player when PlayerFields.Contains(player.Field):
                    Seat(players, player.Seat)[player.Field] = player.Value?.DeepClone();
                    break;
                case ZoneChange zone:
                    Seat(players, zone.Seat)["zones"]![ZoneName(zone.Zone)] = JsonSerializer.SerializeToNode(zone.Cards, TableJson.Options);
                    break;
                default:
                    throw new ArgumentException($"The change {change} is not a change of a view field. Ask for a full view.", nameof(changes));
            }
        }

        return json.Deserialize<TableState>(TableJson.Options)!;
    }

    private static TablePlayer ViewOfPlayer(TablePlayer player, string viewerId)
    {
        var isOwner = player.Id == viewerId;
        var zones = player.Zones;
        foreach (var zone in Zones)
        {
            var cards = player.Zones.Get(zone);
            zones = zones.With(zone, cards
                .Select((card, index) => IsVisible(card, zone, isOwner) ? card : Masked(card, $"hidden-{player.Id}-{ZoneName(zone)}-{index}"))
                .ToList());
        }

        return player with { Zones = zones };
    }

    private static bool IsVisible(TableCard card, TableZone zone, bool isOwner)
    {
        if (zone == TableZone.Library)
        {
            return false;
        }

        return (zone != TableZone.Hand && !card.FaceDown) || isOwner;
    }

    private static TableCard Masked(TableCard card, string id) =>
        new(id, string.Empty, string.Empty, null, false, 0, false, card.Tapped, true, card.Counters);

    private static string ZoneName(TableZone zone) => JsonSerializer.Serialize(zone, TableJson.Options).Trim('"');

    private static JsonNode? ZoneJson(TablePlayer player, TableZone zone) => JsonSerializer.SerializeToNode(player.Zones.Get(zone), TableJson.Options);

    private static bool Same(JsonNode? left, JsonNode? right) => CanonicalJson.Write(left) == CanonicalJson.Write(right);

    private static JsonObject Seat(JsonArray players, string seat) =>
        players.Select(player => player!.AsObject()).FirstOrDefault(player => player["id"]!.GetValue<string>() == seat)
        ?? throw new ArgumentException($"The view has no player \"{seat}\". Ask for a full view.", nameof(seat));
}

/// <summary>
/// One change of a patch. The JSON is the union <c>ViewChange</c> of views.ts, with no type property:
/// <c>{ seat, zone, cards }</c>, <c>{ seat, field, value }</c> or <c>{ field, value }</c>.
/// </summary>
[JsonConverter(typeof(ViewChangeJsonConverter))]
public abstract record ViewChange;

/// <summary>The full new content of one zone of one player in this view.</summary>
/// <param name="Seat">The ID of the player.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Cards">The cards of the zone, from the top.</param>
public sealed record ZoneChange(string Seat, TableZone Zone, IReadOnlyList<TableCard> Cards) : ViewChange;

/// <summary>The new value of one field of one player.</summary>
/// <param name="Seat">The ID of the player.</param>
/// <param name="Field">The field: one of <see cref="PlayerViews.PlayerFields"/>.</param>
/// <param name="Value">The new value, as JSON.</param>
public sealed record PlayerFieldChange(string Seat, string Field, JsonNode? Value) : ViewChange;

/// <summary>The new value of one field of the table.</summary>
/// <param name="Field">The field: one of <see cref="PlayerViews.TableFields"/>.</param>
/// <param name="Value">The new value, as JSON.</param>
public sealed record TableFieldChange(string Field, JsonNode? Value) : ViewChange;

/// <summary>
/// Reads and writes the JSON of a <see cref="ViewChange"/>, as views.ts. The properties tell the kind of change.
/// </summary>
internal sealed class ViewChangeJsonConverter : JsonConverter<ViewChange>
{
    public override ViewChange? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var json = JsonNode.Parse(ref reader)?.AsObject() ?? throw new JsonException("A change of a patch must be an object.");
        string Text(string name) => json[name]?.GetValue<string>() ?? throw new JsonException($"The change of a patch has no \"{name}\".");
        if (json.ContainsKey("zone"))
        {
            return new ZoneChange(
                Text("seat"),
                json["zone"].Deserialize<TableZone>(options),
                json["cards"].Deserialize<List<TableCard>>(options) ?? throw new JsonException("The zone change has no \"cards\"."));
        }

        return json.ContainsKey("seat")
            ? new PlayerFieldChange(Text("seat"), Text("field"), json["value"]?.DeepClone())
            : new TableFieldChange(Text("field"), json["value"]?.DeepClone());
    }

    public override void Write(Utf8JsonWriter writer, ViewChange value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case ZoneChange zone:
                writer.WriteString("seat", zone.Seat);
                writer.WritePropertyName("zone");
                JsonSerializer.Serialize(writer, zone.Zone, options);
                writer.WritePropertyName("cards");
                JsonSerializer.Serialize(writer, zone.Cards, options);
                break;
            case PlayerFieldChange player:
                writer.WriteString("seat", player.Seat);
                writer.WriteString("field", player.Field);
                WriteValue(writer, player.Value, options);
                break;
            case TableFieldChange table:
                writer.WriteString("field", table.Field);
                WriteValue(writer, table.Value, options);
                break;
            default:
                throw new JsonException($"The change {value} is not a change of a view.");
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonNode? value, JsonSerializerOptions options)
    {
        writer.WritePropertyName("value");
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            value.WriteTo(writer, options);
        }
    }
}
