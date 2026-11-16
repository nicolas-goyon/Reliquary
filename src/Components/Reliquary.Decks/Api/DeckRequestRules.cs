using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;

namespace Reliquary.Decks.Api;

/// <summary>
/// The checks of the requests of the deck endpoints. The endpoint groups of the decks share them.
/// </summary>
internal static class DeckRequestRules
{
    internal const int MaxTextLength = 200_000;

    internal const int MaxGroupDecks = 500;

    internal static HashSet<Guid> ValidateDeckIds(List<FieldError> errors, IReadOnlyList<Guid>? deckIds)
    {
        if (deckIds is not { Count: > 0 })
        {
            errors.Add(new FieldError("deckIds", FieldErrorCodes.Required, "Give one deck or more."));
            return [];
        }

        if (deckIds.Count > MaxGroupDecks)
        {
            errors.Add(new FieldError("deckIds", FieldErrorCodes.TooLong, $"Give {MaxGroupDecks} decks or fewer."));
        }

        return [.. deckIds];
    }

    internal static void ValidateDeckTag(List<FieldError> errors, string field, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            errors.Add(new FieldError(field, FieldErrorCodes.Required, "The deck tag is necessary."));
        }
        else if (tag.Length > DeckRules.MaxLabelLength)
        {
            errors.Add(new FieldError(field, FieldErrorCodes.TooLong, $"A deck tag must have {DeckRules.MaxLabelLength} characters or fewer."));
        }
    }

    internal static void AddChoiceError(List<FieldError> errors, string field, string? value, IReadOnlyList<string> choices)
    {
        if (value is not null && !choices.Contains(value))
        {
            errors.Add(new FieldError(field, FieldErrorCodes.NotAllowed, $"Use one of these values: {string.Join(", ", choices)}."));
        }
    }

    /// <summary>
    /// Reads a color identity: the letters W, U, B, R and G in any order and case, or C for colorless.
    /// </summary>
    /// <returns>The letters in the order WUBRG, an empty text for colorless, or <see langword="null"/> for a text that is not valid.</returns>
    internal static string? ReadColors(string colors)
    {
        var letters = colors.Trim().ToUpperInvariant();
        if (letters == "C")
        {
            return string.Empty;
        }

        return letters.Length > 0 && letters.All(DeckRules.ColorOrder.Contains) && letters.Distinct().Count() == letters.Length
            ? string.Concat(DeckRules.ColorOrder.Where(letters.Contains))
            : null;
    }

    internal static void ValidateDeckFields(List<FieldError> errors, string? name, string? format, string? visibility, string? description)
    {
        if (name is { Length: > DeckRules.MaxNameLength })
        {
            errors.Add(new FieldError("name", FieldErrorCodes.TooLong, $"The name of the deck must have {DeckRules.MaxNameLength} characters or fewer."));
        }

        if (format is not null && !DeckRules.Formats.Contains(format))
        {
            errors.Add(new FieldError("format", FieldErrorCodes.NotAllowed, $"The format is not known. Use one of these formats: {string.Join(", ", DeckRules.Formats)}."));
        }

        if (visibility is not null && !DeckRules.Visibilities.Contains(visibility))
        {
            errors.Add(new FieldError("visibility", FieldErrorCodes.NotAllowed, "The visibility must be \"private\", \"unlisted\" or \"public\"."));
        }

        if (description is { Length: > DeckRules.MaxDescriptionLength })
        {
            errors.Add(new FieldError("description", FieldErrorCodes.TooLong, $"The description must have {DeckRules.MaxDescriptionLength} characters or fewer."));
        }
    }

    internal static void ValidateLineFields(List<FieldError> errors, string prefix, string? zone, string? finish, string? note)
    {
        if (zone is not null && !DeckRules.Zones.Contains(zone))
        {
            errors.Add(new FieldError($"{prefix}zone", FieldErrorCodes.NotAllowed, $"The zone must be one of these zones: {string.Join(", ", DeckRules.Zones)}."));
        }

        if (finish is not null && !DeckRules.Finishes.Contains(finish))
        {
            errors.Add(new FieldError($"{prefix}finish", FieldErrorCodes.NotAllowed, "The finish must be \"nonfoil\", \"foil\" or \"etched\"."));
        }

        if (note is { Length: > DeckRules.MaxNoteLength })
        {
            errors.Add(new FieldError($"{prefix}note", FieldErrorCodes.TooLong, $"The note must have {DeckRules.MaxNoteLength} characters or fewer."));
        }
    }

    internal static void ValidateTags(List<FieldError> errors, string prefix, IReadOnlyList<string>? tags, IReadOnlyList<string>? globalTags, IReadOnlyList<string>? categories)
    {
        foreach (var (field, list) in new[] { ("tags", tags), ("globalTags", globalTags) })
        {
            if (list is null)
            {
                continue;
            }

            if (list.Count > DeckRules.MaxTagsPerLine)
            {
                errors.Add(new FieldError($"{prefix}{field}", FieldErrorCodes.TooLong, $"A line can have {DeckRules.MaxTagsPerLine} tags or fewer."));
            }

            for (var index = 0; index < list.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(list[index]))
                {
                    errors.Add(new FieldError($"{prefix}{field}[{index}]", FieldErrorCodes.Required, "A tag must contain a character that is not a space."));
                }
                else if (list[index].Trim().Length > DeckRules.MaxTagLength || list[index].Contains('#', StringComparison.Ordinal))
                {
                    errors.Add(new FieldError($"{prefix}{field}[{index}]", FieldErrorCodes.InvalidFormat, $"A tag must have {DeckRules.MaxTagLength} characters or fewer, and no \"#\"."));
                }
            }
        }

        for (var index = 0; index < (categories?.Count ?? 0); index++)
        {
            if (!DeckRules.Categories.Contains(categories![index]))
            {
                errors.Add(new FieldError($"{prefix}categories[{index}]", FieldErrorCodes.NotAllowed, $"The category must be one of these categories: {string.Join(", ", DeckRules.Categories)}."));
            }
        }
    }

    internal static List<string>? Clean(IReadOnlyList<string>? tags) =>
        tags?.Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Checks the line requests, and finds the card of each line that gives a name.
    /// </summary>
    internal static async Task<IReadOnlyList<LineInput>> ReadLinesAsync(List<FieldError> errors, IReadOnlyList<LineRequest> requests, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        if (requests.Count > DeckRules.MaxLines)
        {
            errors.Add(new FieldError("lines", FieldErrorCodes.TooLong, $"Give {DeckRules.MaxLines} lines or fewer."));
            return [];
        }

        var names = requests.Where(line => line.OracleId is null && !string.IsNullOrWhiteSpace(line.Name)).Select(line => line.Name!).ToList();
        var resolved = names.Count > 0 ? await catalog.ResolveNamesAsync(names, cancellationToken) : new Dictionary<string, CardNameMatch>();
        var inputs = new List<LineInput>();
        for (var index = 0; index < requests.Count; index++)
        {
            var line = requests[index];
            var prefix = $"lines[{index}].";
            var oracleId = line.OracleId;
            if (oracleId is null)
            {
                if (string.IsNullOrWhiteSpace(line.Name))
                {
                    errors.Add(new FieldError($"{prefix}oracleId", FieldErrorCodes.Required, "Give the Oracle ID or the name of the card."));
                }
                else if (resolved.TryGetValue(line.Name, out var match) && match.OracleId is { } found)
                {
                    oracleId = found;
                }
                else
                {
                    errors.Add(new FieldError($"{prefix}name", DeckFieldErrorCodes.CardNotFound, $"No card has the name \"{line.Name}\"."));
                }
            }

            if (line.Quantity is < 1 or > DeckRules.MaxQuantity)
            {
                errors.Add(new FieldError($"{prefix}quantity", FieldErrorCodes.OutOfRange, $"The quantity must be from 1 to {DeckRules.MaxQuantity}."));
            }

            ValidateLineFields(errors, prefix, line.Zone, line.Finish, line.Note);
            ValidateTags(errors, prefix, line.Tags, line.GlobalTags, line.Categories);
            if (oracleId is { } id)
            {
                inputs.Add(new LineInput(
                    id,
                    line.PrintingId,
                    line.Finish,
                    line.Quantity ?? 1,
                    line.Zone ?? "main",
                    line.Note?.Trim() ?? string.Empty,
                    Clean(line.Tags) ?? [],
                    Clean(line.GlobalTags) ?? [],
                    line.Categories ?? []));
            }
        }

        return inputs;
    }
}
