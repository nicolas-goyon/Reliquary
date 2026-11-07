namespace Reliquary.Decks.Versions;

/// <summary>
/// One line of a list for a comparison: a working list or a deck version.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Note">The note.</param>
internal sealed record ComparedLine(Guid OracleId, string Zone, Guid? PrintingId, string? Finish, int Quantity, string Note);

/// <summary>
/// A change of the quantity of a card in a zone.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Before">The quantity in the first list.</param>
/// <param name="After">The quantity in the second list.</param>
internal sealed record QuantityChange(Guid OracleId, string Zone, int Before, int After)
{
    /// <summary>Gets the difference: positive for added cards, negative for removed cards.</summary>
    public int Difference => After - Before;
}

/// <summary>
/// A change of the printings or the finishes of a card in a zone, with the same quantity.
/// </summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Before">The printings and finishes in the first list.</param>
/// <param name="After">The printings and finishes in the second list.</param>
internal sealed record PrintingChange(Guid OracleId, string Zone, IReadOnlyList<(Guid? PrintingId, string? Finish, int Quantity)> Before, IReadOnlyList<(Guid? PrintingId, string? Finish, int Quantity)> After);

/// <summary>
/// A change of the note of a card in a zone.
/// </summary>
internal sealed record NoteChange(Guid OracleId, string Zone, string Before, string After);

/// <summary>
/// The differences between two lists.
/// </summary>
internal sealed record ListComparison(IReadOnlyList<QuantityChange> Quantities, IReadOnlyList<PrintingChange> Printings, IReadOnlyList<NoteChange> Notes);

/// <summary>
/// Compares two lists by Oracle card and zone. See docs/decisions/0033-deck-versions-and-variants.md.
/// </summary>
internal static class ListComparer
{
    public static ListComparison Compare(IEnumerable<ComparedLine> first, IEnumerable<ComparedLine> second)
    {
        var before = first.GroupBy(line => (line.OracleId, line.Zone)).ToDictionary(group => group.Key, group => group.ToList());
        var after = second.GroupBy(line => (line.OracleId, line.Zone)).ToDictionary(group => group.Key, group => group.ToList());
        var quantities = new List<QuantityChange>();
        var printings = new List<PrintingChange>();
        var notes = new List<NoteChange>();
        foreach (var key in before.Keys.Union(after.Keys).OrderBy(key => key.Zone, StringComparer.Ordinal).ThenBy(key => key.OracleId))
        {
            var oldLines = before.GetValueOrDefault(key) ?? [];
            var newLines = after.GetValueOrDefault(key) ?? [];
            var oldQuantity = oldLines.Sum(line => line.Quantity);
            var newQuantity = newLines.Sum(line => line.Quantity);
            if (oldQuantity != newQuantity)
            {
                quantities.Add(new QuantityChange(key.OracleId, key.Zone, oldQuantity, newQuantity));
            }
            else
            {
                var oldPrintings = Printings(oldLines);
                var newPrintings = Printings(newLines);
                if (!oldPrintings.SequenceEqual(newPrintings))
                {
                    printings.Add(new PrintingChange(key.OracleId, key.Zone, oldPrintings, newPrintings));
                }
            }

            var oldNote = string.Join(' ', oldLines.Select(line => line.Note).Where(note => note.Length > 0));
            var newNote = string.Join(' ', newLines.Select(line => line.Note).Where(note => note.Length > 0));
            if (oldNote != newNote && (oldLines.Count > 0 && newLines.Count > 0))
            {
                notes.Add(new NoteChange(key.OracleId, key.Zone, oldNote, newNote));
            }
        }

        return new ListComparison(quantities, printings, notes);
    }

    private static List<(Guid? PrintingId, string? Finish, int Quantity)> Printings(IEnumerable<ComparedLine> lines) =>
        [.. lines
            .GroupBy(line => (line.PrintingId, line.Finish))
            .Select(group => (group.Key.PrintingId, group.Key.Finish, group.Sum(line => line.Quantity)))
            .OrderBy(item => item.PrintingId)
            .ThenBy(item => item.Finish, StringComparer.Ordinal)];
}
