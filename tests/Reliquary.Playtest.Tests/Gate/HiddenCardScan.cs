using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Reliquary.Playtest.Engine;
using Reliquary.Playtest.Views;

namespace Reliquary.Playtest.Tests.Gate;

/// <summary>
/// Scans the raw frames that one seat got, and finds the card IDs and the Oracle IDs of the hidden cards of the other seats.
/// </summary>
/// <remarks>
/// <para>A hidden card is a card in the hand or in the library of another seat, or a face-down card of another seat.
/// A card can be public at one sequence number and hidden at the next one. Thus the scan uses the state of the server
/// at the sequence number of each message: a patch has <c>seq</c>, and a snapshot has <c>seq</c> in its result.</para>
/// <para>A message with no sequence number (the lobby, the result of <c>Act</c>) must have no card ID of another seat at all.</para>
/// <para>Two copies of a card have the same Oracle ID. An Oracle ID is a leak only when the seat sees no copy with this Oracle ID.</para>
/// </remarks>
internal static partial class HiddenCardScan
{
    private const char RecordSeparator = '\u001e';

    /// <summary>
    /// Scans the frames of one seat.
    /// </summary>
    /// <param name="seat">The seat that got the frames.</param>
    /// <param name="frames">The raw text frames.</param>
    /// <param name="truth">The states of the server.</param>
    /// <returns>The leaks that the scan found, and the number of messages that it scanned.</returns>
    public static async Task<(List<string> Leaks, int Messages)> ScanAsync(string seat, IEnumerable<string> frames, GateTruth truth)
    {
        var leaks = new List<string>();
        var messages = 0;
        var start = await truth.StateAtAsync(truth.StartSeq);
        var allOtherIds = start.Players.Where(player => player.Id != seat)
            .SelectMany(player => Enum.GetValues<TableZone>().SelectMany(zone => player.Zones.Get(zone)))
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        var cache = new Dictionary<long, (HashSet<string> Ids, HashSet<string> Oracles)>();

        foreach (var frame in frames)
        {
            foreach (var record in frame.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                messages++;
                var seq = SeqOf(record);
                HashSet<string> ids;
                HashSet<string> oracles;
                // The messages before the start have no table: they must have no card of another seat.
                if (seq is { } number && number >= truth.StartSeq)
                {
                    if (!cache.TryGetValue(number, out var hidden))
                    {
                        hidden = Hidden(await truth.StateAtAsync(number), seat);
                        cache[number] = hidden;
                    }

                    (ids, oracles) = hidden;
                }
                else
                {
                    (ids, oracles) = (allOtherIds, []);
                }

                foreach (Match token in Token().Matches(record))
                {
                    if (ids.Contains(token.Value) || oracles.Contains(token.Value))
                    {
                        leaks.Add($"Seat {seat} got the hidden value {token.Value} at the sequence number {seq?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}.");
                    }
                }
            }
        }

        return (leaks, messages);
    }

    private static (HashSet<string> Ids, HashSet<string> Oracles) Hidden(TableState state, string seat)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var oracles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in state.Players.Where(player => player.Id != seat))
        {
            foreach (var zone in Enum.GetValues<TableZone>())
            {
                foreach (var card in player.Zones.Get(zone).Where(card => zone is TableZone.Hand or TableZone.Library || card.FaceDown))
                {
                    ids.Add(card.Id);
                    if (card.OracleId.Length > 0)
                    {
                        oracles.Add(card.OracleId);
                    }
                }
            }
        }

        var view = PlayerViews.View(state, seat);
        oracles.ExceptWith(view.Players.SelectMany(player => Enum.GetValues<TableZone>().SelectMany(zone => player.Zones.Get(zone))).Select(card => card.OracleId));
        return (ids, oracles);
    }

    private static long? SeqOf(string record)
    {
        var message = JsonNode.Parse(record)?.AsObject();
        if (message is null || !message.ContainsKey("type"))
        {
            return null;
        }

        return message["type"]!.GetValue<int>() switch
        {
            1 when message["target"]?.GetValue<string>() == "Patch" => message["arguments"]![0]!["seq"]!.GetValue<long>(),
            3 when message["result"] is JsonObject result && result.ContainsKey("view") => result["seq"]!.GetValue<long>(),
            _ => null,
        };
    }

    /// <summary>A word of letters, digits, hyphens and underscores: the card IDs (base64url) and the Oracle IDs (GUID).</summary>
    [GeneratedRegex("[A-Za-z0-9_-]+")]
    private static partial Regex Token();
}
