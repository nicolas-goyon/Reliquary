using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Collection.Api;
using Reliquary.Collection.Data;

namespace Reliquary.Collection;

/// <summary>
/// A card of a request, after the checks: the card fields, the copies of the name and the set code, and the quantity.
/// </summary>
/// <param name="Line">The card fields with the name and the set code.</param>
/// <param name="Quantity">The number of copies.</param>
internal sealed record ResolvedCard(CardLine Line, int Quantity);

/// <summary>
/// Checks the cards of the requests with the card catalog: the card exists, and the printing is a printing of the card.
/// </summary>
internal sealed class CardResolver(ICardCatalog catalog)
{
    /// <summary>
    /// Checks the cards of a request, and finds the card of each name.
    /// </summary>
    /// <param name="input">The cards of the request.</param>
    /// <param name="field">The name of the list in the request, for example <c>entries</c>.</param>
    /// <param name="settings">The advanced fields of the owner. The method ignores the disabled fields.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The cards, in the order of the request.</returns>
    /// <exception cref="CollectionException">A card is not valid.</exception>
    public async Task<IReadOnlyList<ResolvedCard>> ResolveAsync(IReadOnlyList<CardRequest>? input, string field, AdvancedFields settings, CancellationToken cancellationToken)
    {
        var requests = input?.Select(settings.Input).ToList();
        var errors = new List<FieldError>();
        if (requests is not { Count: > 0 })
        {
            throw CollectionException.Validation(new FieldError(field, FieldErrorCodes.Required, "Give one card or more."));
        }

        if (requests.Count > CollectionRules.MaxCardsPerRequest)
        {
            throw CollectionException.Validation(new FieldError(field, FieldErrorCodes.TooLong, $"Give {CollectionRules.MaxCardsPerRequest} cards or fewer."));
        }

        var names = requests.Where(card => card.OracleId is null && !string.IsNullOrWhiteSpace(card.Name)).Select(card => card.Name!).Distinct().ToList();
        var resolved = names.Count > 0 ? await catalog.ResolveNamesAsync(names, cancellationToken) : new Dictionary<string, CardNameMatch>();
        var oracleIds = new Guid?[requests.Count];
        for (var index = 0; index < requests.Count; index++)
        {
            var card = requests[index];
            if (card.OracleId is { } id)
            {
                oracleIds[index] = id;
            }
            else if (string.IsNullOrWhiteSpace(card.Name))
            {
                errors.Add(new FieldError($"{field}[{index}].oracleId", FieldErrorCodes.Required, "Give the Oracle ID or the name of the card."));
            }
            else if (resolved.TryGetValue(card.Name, out var match) && match.OracleId is { } found)
            {
                oracleIds[index] = found;
            }
            else
            {
                errors.Add(new FieldError($"{field}[{index}].name", CollectionFieldErrorCodes.CardNotFound, $"No card has the name \"{card.Name}\"."));
            }
        }

        var cards = await catalog.GetCardsAsync([.. oracleIds.OfType<Guid>()], cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. requests.Where(card => card.PrintingId is not null).Select(card => card.PrintingId!.Value).Distinct()], cancellationToken);
        var result = new List<ResolvedCard>();
        for (var index = 0; index < requests.Count; index++)
        {
            var card = requests[index];
            var prefix = $"{field}[{index}].";
            var count = errors.Count;
            CheckFields(errors, prefix, card);
            if (oracleIds[index] is not { } oracleId)
            {
                continue;
            }

            if (!cards.TryGetValue(oracleId, out var facts))
            {
                errors.Add(new FieldError($"{prefix}oracleId", FieldErrorCodes.NotAllowed, "The card does not exist."));
                continue;
            }

            PrintingFacts? printing = null;
            if (card.PrintingId is { } printingId && (!printings.TryGetValue(printingId, out printing) || printing.OracleId != oracleId))
            {
                errors.Add(new FieldError($"{prefix}printingId", FieldErrorCodes.NotAllowed, "The printing does not exist, or it is not a printing of the card."));
                continue;
            }

            var language = card.Language ?? printing?.Lang ?? "en";
            if (printing is not null && card.Language is not null && card.Language != printing.Lang)
            {
                errors.Add(new FieldError($"{prefix}language", FieldErrorCodes.NotAllowed, $"The printing is in the language \"{printing.Lang}\". Give this language, or give no language."));
            }

            if (printing is not null && card.Finish is not null && CollectionRules.Finishes.Contains(card.Finish) && !printing.Finishes.Contains(card.Finish))
            {
                errors.Add(new FieldError($"{prefix}finish", FieldErrorCodes.NotAllowed, $"The printing has these finishes only: {string.Join(", ", printing.Finishes)}."));
            }

            if (errors.Count > count)
            {
                continue;
            }

            var note = string.IsNullOrWhiteSpace(card.Note) ? null : card.Note.Trim();
            var fields = new CardFields(
                oracleId,
                printing?.PrintingId,
                language,
                card.Finish,
                card.Condition,
                card.PurchasePrice,
                card.PurchasePrice is null ? null : card.PurchaseCurrency?.ToUpperInvariant(),
                card.AcquiredOn,
                note);
            result.Add(new ResolvedCard(new CardLine(fields, facts.Name, printing?.SetCode), card.Quantity ?? 1));
        }

        if (errors.Count > 0)
        {
            throw CollectionException.Validation([.. errors]);
        }

        return result;
    }

    /// <summary>
    /// Checks the fields of a card that do not need the card catalog.
    /// </summary>
    private static void CheckFields(List<FieldError> errors, string prefix, CardRequest card)
    {
        if (card.Quantity is < 1 or > CollectionRules.MaxQuantity)
        {
            errors.Add(new FieldError($"{prefix}quantity", FieldErrorCodes.OutOfRange, $"The quantity must be from 1 to {CollectionRules.MaxQuantity}."));
        }

        CheckCardFields(errors, prefix, card.Language, card.Finish, card.Condition, card.PurchasePrice, card.PurchaseCurrency, card.Note);
    }

    /// <summary>
    /// Checks the language and the advanced fields of a card.
    /// </summary>
    public static void CheckCardFields(List<FieldError> errors, string prefix, string? language, string? finish, string? condition, decimal? purchasePrice, string? purchaseCurrency, string? note)
    {
        if (language is not null && !CollectionRules.Languages.Contains(language))
        {
            errors.Add(new FieldError($"{prefix}language", FieldErrorCodes.NotAllowed, $"The language must be one of these codes: {string.Join(", ", CollectionRules.Languages)}."));
        }

        if (finish is not null && !CollectionRules.Finishes.Contains(finish))
        {
            errors.Add(new FieldError($"{prefix}finish", FieldErrorCodes.NotAllowed, "The finish must be \"nonfoil\", \"foil\" or \"etched\"."));
        }

        if (condition is not null && !CollectionRules.Conditions.Contains(condition))
        {
            errors.Add(new FieldError($"{prefix}condition", FieldErrorCodes.NotAllowed, $"The condition must be one of these values: {string.Join(", ", CollectionRules.Conditions)}."));
        }

        if (purchasePrice is < 0 or > CollectionRules.MaxPrice || (purchasePrice is { } price && decimal.Round(price, 2) != price))
        {
            errors.Add(new FieldError($"{prefix}purchasePrice", FieldErrorCodes.OutOfRange, "The purchase price must be a positive number with 2 decimals or fewer."));
        }

        if (purchasePrice is not null && (purchaseCurrency is null || purchaseCurrency.Length != 3 || !purchaseCurrency.All(char.IsAsciiLetter)))
        {
            errors.Add(new FieldError($"{prefix}purchaseCurrency", FieldErrorCodes.InvalidFormat, "Give the currency of the price as a code of 3 letters, for example EUR or USD."));
        }

        if (note is { Length: > CollectionRules.MaxNoteLength })
        {
            errors.Add(new FieldError($"{prefix}note", FieldErrorCodes.TooLong, $"The note must have {CollectionRules.MaxNoteLength} characters or fewer."));
        }
    }
}
