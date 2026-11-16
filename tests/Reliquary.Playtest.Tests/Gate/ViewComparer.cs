using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Tests.Gate;

/// <summary>
/// Compares two views field by field. It is the deep equality of the gate test, and it is much faster than a comparison of the JSON.
/// </summary>
internal static class ViewComparer
{
    private static readonly TableZone[] Zones = Enum.GetValues<TableZone>();

    /// <summary>
    /// Finds the first difference between two views.
    /// </summary>
    /// <returns>The difference, or <see langword="null"/> when the views are equal.</returns>
    public static string? Difference(TableState expected, TableState actual)
    {
        if (expected.Commander != actual.Commander || expected.ActivePlayerId != actual.ActivePlayerId || expected.Turn != actual.Turn
            || expected.Seed != actual.Seed || expected.NextTokenId != actual.NextTokenId)
        {
            return $"table fields: expected ({expected.Commander}, {expected.ActivePlayerId}, {expected.Turn}, {expected.Seed}, {expected.NextTokenId}), "
                + $"actual ({actual.Commander}, {actual.ActivePlayerId}, {actual.Turn}, {actual.Seed}, {actual.NextTokenId})";
        }

        if (expected.Players.Count != actual.Players.Count)
        {
            return $"{expected.Players.Count} players expected, {actual.Players.Count} actual";
        }

        for (var index = 0; index < expected.Players.Count; index++)
        {
            if (Difference(expected.Players[index], actual.Players[index]) is { } difference)
            {
                return $"player {expected.Players[index].Id}: {difference}";
            }
        }

        return null;
    }

    private static string? Difference(TablePlayer expected, TablePlayer actual)
    {
        if (expected.Id != actual.Id || expected.Name != actual.Name || expected.Life != actual.Life || expected.Counters != actual.Counters
            || expected.Mulligans != actual.Mulligans || expected.CardsToBottom != actual.CardsToBottom)
        {
            return $"fields: expected {expected with { Zones = TableZones.Empty }}, actual {actual with { Zones = TableZones.Empty }}";
        }

        if (expected.CommanderDamage.Count != actual.CommanderDamage.Count
            || expected.CommanderDamage.Any(pair => !actual.CommanderDamage.TryGetValue(pair.Key, out var value) || value != pair.Value))
        {
            return "commander damage";
        }

        foreach (var zone in Zones)
        {
            var left = expected.Zones.Get(zone);
            var right = actual.Zones.Get(zone);
            if (left.Count != right.Count)
            {
                return $"{zone}: {left.Count} cards expected, {right.Count} actual";
            }

            for (var position = 0; position < left.Count; position++)
            {
                // TableCard has only values, so the record equality compares all its fields.
                if (left[position] != right[position])
                {
                    return $"{zone} {position}: expected {left[position]}, actual {right[position]}";
                }
            }
        }

        return null;
    }
}
