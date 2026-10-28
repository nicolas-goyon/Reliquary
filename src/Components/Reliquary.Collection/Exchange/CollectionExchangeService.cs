using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Collection.Data;

namespace Reliquary.Collection.Exchange;

/// <summary>
/// The result of an import.
/// </summary>
/// <param name="BatchId">The batch of the movements, or <see langword="null"/> when the import added no card.</param>
/// <param name="LineCount">The number of lines that the import added.</param>
/// <param name="CardCount">The number of copies that the import added.</param>
/// <param name="Errors">The lines that the import cannot use. The import ignored these lines.</param>
/// <param name="Warnings">The lines that the import changed, for example a card with no printing.</param>
internal sealed record ImportResult(Guid? BatchId, int LineCount, int CardCount, IReadOnlyList<FileLineError> Errors, IReadOnlyList<FileLineError> Warnings);

/// <summary>
/// A file of an export.
/// </summary>
/// <param name="FileName">The name of the file.</param>
/// <param name="ContentType">The media type of the file.</param>
/// <param name="Content">The content of the file.</param>
internal sealed record ExportedFile(string FileName, string ContentType, string Content);

/// <summary>
/// Imports cards from a file into a location, and exports locations to a file, in the formats of the other tools.
/// </summary>
internal sealed class CollectionExchangeService(CollectionStore store, EntryService entries, ICardCatalog catalog)
{
    /// <summary>
    /// Adds the cards of a file to a location. The import adds the lines that it can use, and gives the other lines with their numbers.
    /// </summary>
    /// <param name="ownerId">The owner of the collection.</param>
    /// <param name="locationId">The location or the section that gets the cards.</param>
    /// <param name="format">The format of the file.</param>
    /// <param name="content">The content of the file.</param>
    /// <param name="origin">The origin of the movements.</param>
    /// <param name="currency">The currency of a purchase price that has no currency in the file.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <exception cref="CollectionException">The location does not exist, is locked or is an assembled deck. Or the file has too many cards.</exception>
    public async Task<ImportResult> ImportAsync(
        Guid ownerId, Guid locationId, ICollectionFormat format, string content, MovementEndpoint origin, string currency, CancellationToken cancellationToken)
    {
        AdvancedFields settings;
        await using (var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken))
        {
            var location = await CollectionStore.FindLocationAsync(connection, ownerId, locationId, cancellationToken) ?? throw CollectionException.LocationNotFound();
            if (location.DeckId is not null)
            {
                throw CollectionException.DeckLocation();
            }

            if (location.EffectiveLocked)
            {
                throw CollectionException.Locked(location.FullName);
            }

            settings = await SettingsService.GetAsync(connection, ownerId, cancellationToken);
        }

        var parsed = format.Parse(content);
        if (parsed.Cards.Count > CollectionRules.MaxImportCards)
        {
            throw CollectionException.Validation(new FieldError("content", FieldErrorCodes.TooLong, $"The file has {parsed.Cards.Count} card lines. Give a file with {CollectionRules.MaxImportCards} card lines or fewer."));
        }

        var source = content.Split('\n');
        string Text(int line) => line >= 1 && line <= source.Length ? source[line - 1].TrimEnd('\r') : string.Empty;
        var errors = new List<FileLineError>(parsed.Errors);
        var warnings = new List<FileLineError>();
        var names = parsed.Cards.Count > 0
            ? await catalog.ResolveNamesAsync([.. parsed.Cards.Select(card => card.Name).Distinct(StringComparer.Ordinal)], cancellationToken)
            : new Dictionary<string, CardNameMatch>();
        var references = new Dictionary<FileCard, PrintingReference>();
        foreach (var card in parsed.Cards.Where(card => card.SetCode is not null && card.CollectorNumber is not null))
        {
            FileValues.TryReadLanguage(card.Language, out var language);
            references[card] = new PrintingReference(card.SetCode!, card.CollectorNumber!, language);
        }

        var printings = references.Count > 0
            ? await catalog.FindPrintingsAsync([.. references.Values.Distinct()], cancellationToken)
            : new Dictionary<PrintingReference, PrintingFacts>();
        var cards = new List<ResolvedCard>();
        foreach (var card in parsed.Cards)
        {
            var (fields, code) = ReadFields(card, settings, currency);
            if (code is null && names.GetValueOrDefault(card.Name) is not { OracleId: not null })
            {
                code = FileLineCodes.CardNotFound;
            }

            if (code is not null)
            {
                errors.Add(new FileLineError(card.LineNumber, code, Text(card.LineNumber)));
                continue;
            }

            var match = names[card.Name];
            var oracleId = match.OracleId!.Value;
            PrintingFacts? printing = null;
            if (references.TryGetValue(card, out var reference))
            {
                if (printings.TryGetValue(reference, out var found) && found.OracleId == oracleId)
                {
                    printing = found;
                }
                else
                {
                    warnings.Add(new FileLineError(card.LineNumber, FileLineCodes.PrintingNotFound, Text(card.LineNumber)));
                }
            }

            fields = fields with { OracleId = oracleId, PrintingId = printing?.PrintingId, Language = fields.Language.Length > 0 ? fields.Language : printing?.Lang ?? "en" };
            if (fields.Finish is { } finish && printing is not null && !printing.Finishes.Contains(finish))
            {
                warnings.Add(new FileLineError(card.LineNumber, FileLineCodes.FinishNotAvailable, Text(card.LineNumber)));
                fields = fields with { Finish = null };
            }

            cards.Add(new ResolvedCard(new CardLine(fields, match.Name ?? card.Name, printing?.SetCode), card.Quantity));
        }

        Guid? batchId = null;
        if (cards.Count > 0)
        {
            batchId = (await entries.AddAsync(ownerId, locationId, cards, origin, cancellationToken)).BatchId;
        }

        return new ImportResult(
            batchId,
            cards.Count,
            cards.Sum(card => card.Quantity),
            [.. errors.OrderBy(error => error.Line)],
            [.. warnings.OrderBy(warning => warning.Line)]);
    }

    /// <summary>
    /// Writes some locations to a file.
    /// </summary>
    /// <param name="ownerId">The owner of the collection.</param>
    /// <param name="locationId">The location, or <see langword="null"/> for all the collection.</param>
    /// <param name="sections">Whether the file contains the cards of the sections of the location.</param>
    /// <param name="format">The format of the file.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <exception cref="CollectionException">The location does not exist.</exception>
    public async Task<ExportedFile> ExportAsync(Guid ownerId, Guid? locationId, bool sections, ICollectionFormat format, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        var all = await CollectionStore.ListLocationsAsync(connection, ownerId, cancellationToken);
        var byId = all.ToDictionary(location => location.Id);
        var ordered = all.Where(location => location.ParentId is null)
            .SelectMany(location => all.Where(section => section.ParentId == location.Id).Prepend(location))
            .ToList();
        string FullName(LocationSummaryRow location) =>
            location.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? $"{parent.Name} / {location.Name}" : location.Name;

        var fileStem = "collection";
        if (locationId is { } id)
        {
            var location = byId.GetValueOrDefault(id) ?? throw CollectionException.LocationNotFound();
            ordered = [.. ordered.Where(row => row.Id == id || (sections && row.ParentId == id))];
            fileStem = location.Name;
        }

        var rows = await CollectionStore.ListEntriesAsync(connection, ownerId, [.. ordered.Select(location => location.Id)], cancellationToken);
        var settings = await SettingsService.GetAsync(connection, ownerId, cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. rows.Where(row => row.PrintingId is not null).Select(row => row.PrintingId!.Value).Distinct()], cancellationToken);
        var rowsByLocation = rows.ToLookup(row => row.LocationId);
        var cards = ordered.SelectMany(location => rowsByLocation[location.Id].Select(row =>
        {
            var fields = settings.Output(row.Fields);
            var printing = row.PrintingId is { } printingId ? printings.GetValueOrDefault(printingId) : null;
            return new ExportCard(
                row.CardName,
                printing is null ? null : row.SetCode,
                printing?.CollectorNumber,
                row.Language,
                row.Quantity,
                fields.Finish,
                fields.Condition,
                fields.PurchasePrice,
                fields.PurchasePrice is null ? null : fields.PurchaseCurrency,
                fields.AcquiredOn,
                fields.Note,
                FullName(location));
        })).ToList();
        return new ExportedFile(FileName(fileStem) + format.Extension, format.ContentType, format.Write(cards));
    }

    /// <summary>
    /// Makes a safe file name from the name of a location: letters, digits, spaces, hyphens and underscores.
    /// </summary>
    internal static string FileName(string name)
    {
        var safe = new string([.. name.Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' ? character : '_')]).Trim();
        return safe.Any(char.IsLetterOrDigit) ? safe : "collection";
    }

    /// <summary>
    /// Reads the language and the advanced fields of a card. The method ignores the fields that the owner did not enable.
    /// </summary>
    /// <returns>The fields with no card, and the reason code of the first value that is not valid, or <see langword="null"/>.</returns>
    private static (CardFields Fields, string? Code) ReadFields(FileCard card, AdvancedFields settings, string currency)
    {
        var fields = new CardFields(Guid.Empty, null, string.Empty, null, null, null, null, null, null);
        if (!FileValues.TryReadLanguage(card.Language, out var language))
        {
            return (fields, FileLineCodes.UnknownLanguage);
        }

        fields = fields with { Language = language ?? string.Empty };
        if (settings.Finish)
        {
            fields = fields with { Finish = FileValues.ReadFinish(card.Finish) };
        }

        if (settings.Condition)
        {
            if (!FileValues.TryReadCondition(card.Condition, out var condition))
            {
                return (fields, FileLineCodes.UnknownCondition);
            }

            fields = fields with { Condition = condition };
        }

        if (settings.PurchasePrice)
        {
            if (!FileValues.TryReadPrice(card.PurchasePrice, out var price, out var symbolCurrency))
            {
                return (fields, FileLineCodes.InvalidPrice);
            }

            if (price is not null)
            {
                var priceCurrency = card.PurchaseCurrency is null ? symbolCurrency ?? currency : FileValues.ReadCurrency(card.PurchaseCurrency);
                if (priceCurrency is null)
                {
                    return (fields, FileLineCodes.InvalidPrice);
                }

                fields = fields with { PurchasePrice = price, PurchaseCurrency = priceCurrency };
            }
        }

        if (settings.AcquiredOn)
        {
            if (!FileValues.TryReadDate(card.AcquiredOn, out var date))
            {
                return (fields, FileLineCodes.InvalidDate);
            }

            fields = fields with { AcquiredOn = date };
        }

        if (settings.Note && card.Note is { } note && note.Trim().Length > 0)
        {
            if (note.Trim().Length > CollectionRules.MaxNoteLength)
            {
                return (fields, FileLineCodes.NoteTooLong);
            }

            fields = fields with { Note = note.Trim() };
        }

        return (fields, null);
    }
}
