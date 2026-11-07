using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.AutoTag;

/// <summary>
/// The suggestion of auto-tag for one deck line.
/// </summary>
/// <param name="Line">The line.</param>
/// <param name="Name">The Oracle name of the card.</param>
/// <param name="Suggestion">The suggested categories.</param>
internal sealed record LineSuggestion(DeckLineRow Line, string Name, AutoTagSuggestion Suggestion);

/// <summary>
/// The categories that the user chose for one deck line.
/// </summary>
/// <param name="LineId">The line.</param>
/// <param name="Categories">The categories. They replace the categories of the line.</param>
internal sealed record CategoryChoice(Guid LineId, IReadOnlyList<string> Categories);

/// <summary>
/// Suggests the categories of the lines of a deck, and applies the categories that the user accepts. See ADR 0031.
/// </summary>
internal sealed class AutoTagService(DeckStore store, DeckService decks, ICardCatalog catalog, TimeProvider time)
{
    /// <summary>
    /// Suggests the categories of each line of a deck of the owner. The lines of the tokens zone and the extra cards get no suggestion.
    /// </summary>
    /// <exception cref="DeckException">The deck does not exist, or it is not a deck of the user.</exception>
    public async Task<(DeckRow Deck, IReadOnlyList<LineSuggestion> Lines)> SuggestAsync(Guid ownerId, Guid deckId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var deck = await DeckStore.FindDeckAsync(connection, ownerId, deckId, cancellationToken) ?? throw DeckException.NotFound();
        var lines = await DeckStore.ListLinesAsync(connection, deckId, cancellationToken);
        var oracleIds = lines.Select(line => line.OracleId).Distinct().ToList();
        var rules = await CategoryRuleStore.ListAsync(connection, ownerId, oracleIds, cancellationToken);
        var suggestions = await SuggestAsync(lines, rules, cancellationToken);
        return (deck, suggestions);
    }

    /// <summary>
    /// Applies the categories that the user chose, in one change of the deck. The deck gets one new version.
    /// A choice that differs from the automatic suggestion becomes a rule of the user. A choice that is equal to it removes the rule.
    /// </summary>
    /// <exception cref="DeckException">The deck does not exist, a line does not exist, or the deck has another version.</exception>
    public async Task<DeckView> ApplyAsync(Guid ownerId, Guid deckId, IReadOnlyList<CategoryChoice> choices, int? expectedVersion, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(ownerId, deckId, cancellationToken);
        if (!view.IsOwner)
        {
            throw DeckException.NotFound();
        }

        // The automatic suggestion does not use the rules of the user.
        var automatic = (await SuggestAsync(view.Lines, new Dictionary<Guid, string[]>(), cancellationToken))
            .GroupBy(item => item.Line.OracleId)
            .ToDictionary(group => group.Key, group => group.First().Suggestion.Categories);

        return await decks.MutateAsync(ownerId, deckId, expectedVersion, [], async (_, transaction, _, lines, _) =>
        {
            var errors = new List<FieldError>();
            var now = time.GetUtcNow().UtcDateTime;
            var changes = new List<(DeckLineRow Line, string[] Categories)>();
            for (var index = 0; index < choices.Count; index++)
            {
                var line = lines.FirstOrDefault(candidate => candidate.Id == choices[index].LineId);
                if (line is null)
                {
                    errors.Add(new FieldError($"lines[{index}].lineId", FieldErrorCodes.NotAllowed, "The deck has no line with this ID. Read the suggestions again."));
                    continue;
                }

                changes.Add((line, [.. DeckRules.Categories.Where(choices[index].Categories.Contains)]));
            }

            if (errors.Count > 0)
            {
                throw DeckException.Validation([.. errors]);
            }

            foreach (var (line, categories) in changes)
            {
                line.Categories = categories;
                line.UpdatedAt = now;
                var suggested = automatic.GetValueOrDefault(line.OracleId) ?? [];
                if (suggested.ToHashSet().SetEquals(categories))
                {
                    await CategoryRuleStore.DeleteAsync(transaction, ownerId, line.OracleId, cancellationToken);
                }
                else
                {
                    await CategoryRuleStore.UpsertAsync(transaction, ownerId, line.OracleId, categories, now, cancellationToken);
                }
            }
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<LineSuggestion>> SuggestAsync(IReadOnlyList<DeckLineRow> lines, IReadOnlyDictionary<Guid, string[]> rules, CancellationToken cancellationToken)
    {
        var oracleIds = lines.Select(line => line.OracleId).Distinct().ToList();
        var cards = await catalog.GetCardsAsync(oracleIds, cancellationToken);
        var tags = await catalog.GetOracleTagsAsync(oracleIds, cancellationToken);
        return [.. lines
            .Where(line => line.Zone != "tokens" && cards.TryGetValue(line.OracleId, out var card) && !card.IsExtra)
            .Select(line => new LineSuggestion(
                line,
                cards[line.OracleId].Name,
                AutoTagger.Suggest(cards[line.OracleId], tags.GetValueOrDefault(line.OracleId) ?? [], rules.GetValueOrDefault(line.OracleId))))];
    }
}
