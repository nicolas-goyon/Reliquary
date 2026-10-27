using System.IO.Compression;
using System.Text;
using Reliquary.Decks.Data;
using Reliquary.Decks.Text;

namespace Reliquary.Decks.Exchange;

/// <summary>
/// A file of an exported deck.
/// </summary>
/// <param name="FileName">The name of the file.</param>
/// <param name="ContentType">The media type of the file.</param>
/// <param name="Content">The content of the file.</param>
internal sealed record ExportedDeck(string FileName, string ContentType, string Content);

/// <summary>
/// Imports a deck from a file and exports a deck to a file, in the formats of the other tools.
/// </summary>
internal sealed class DeckExchangeService(DeckService decks, DeckTextService texts)
{
    /// <summary>
    /// Makes a new deck from a file.
    /// </summary>
    /// <returns>The deck and the warnings of the lines.</returns>
    /// <exception cref="DeckTextException">A line has an error. The API makes no deck.</exception>
    public async Task<(DeckRow Deck, IReadOnlyList<(int Line, string Code, string Text)> Warnings)> ImportAsync(
        Guid ownerId, IDeckFormat format, string content, NewDeck deck, CancellationToken cancellationToken)
    {
        var sourceLines = content.Split('\n');
        var parsed = format.Parse(content);
        var (lines, warnings) = await texts.ResolveAsync(
            parsed,
            number => number >= 1 && number <= sourceLines.Length ? sourceLines[number - 1].TrimEnd('\r') : string.Empty,
            keepUnknownPrintings: true,
            cancellationToken);
        return (await decks.CreateAsync(ownerId, deck, lines, cancellationToken), warnings);
    }

    public async Task<ExportedDeck> ExportAsync(Guid? viewerId, Guid deckId, IDeckFormat format, CancellationToken cancellationToken)
    {
        var view = await decks.GetAsync(viewerId, deckId, cancellationToken);
        var lines = await texts.ToTextLinesAsync(view, cancellationToken);
        var content = format.Write(view.Deck.Format, lines);
        return new ExportedDeck(FileName(view.Deck.Name) + format.Extension, format.ContentType, content);
    }

    /// <summary>
    /// Exports several decks of the owner into one ZIP file, with one file for each deck.
    /// Two decks with the same name get the files "Name.txt" and "Name (2).txt".
    /// </summary>
    /// <exception cref="DeckException">A deck does not exist.</exception>
    public async Task<byte[]> ExportArchiveAsync(Guid ownerId, IReadOnlyCollection<Guid> deckIds, IDeckFormat format, CancellationToken cancellationToken)
    {
        var files = new List<ExportedDeck>();
        foreach (var deckId in deckIds)
        {
            var view = await decks.GetAsync(ownerId, deckId, cancellationToken);
            if (!view.IsOwner)
            {
                throw DeckException.NotFound();
            }

            files.Add(await ExportAsync(ownerId, deckId, format, cancellationToken));
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var stem = file.FileName[..^format.Extension.Length];
                var name = file.FileName;
                for (var number = 2; !used.Add(name); number++)
                {
                    name = $"{stem} ({number}){format.Extension}";
                }

                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                await using var entryStream = await entry.OpenAsync(cancellationToken);
                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(file.Content), cancellationToken);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Makes a safe file name from the name of a deck: letters, digits, spaces, hyphens and underscores.
    /// </summary>
    internal static string FileName(string deckName)
    {
        var safe = new string([.. deckName.Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' ? character : '_')]).Trim();
        return safe.Any(char.IsLetterOrDigit) ? safe : "deck";
    }
}
