namespace Reliquary.Cards.Import;

/// <summary>
/// The settings of the card data import. The section is <c>Cards:Import</c>.
/// </summary>
public sealed class CardImportOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Cards:Import";

    /// <summary>
    /// Gets or sets whether the worker runs the daily import.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum time between two imports. Scryfall updates its files every 12 hours.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Gets or sets the Scryfall bulk file of the cards:
    /// <c>all_cards</c> (all the languages, about 400 MB) or <c>default_cards</c> (English or the only language, about 80 MB).
    /// </summary>
    public string BulkType { get; set; } = "all_cards";

    /// <summary>
    /// Gets or sets a folder with local files. When it has a value, the import reads the files of this folder and downloads nothing.
    /// </summary>
    /// <remarks>
    /// The import reads these files, each with or without the extension <c>.gz</c>:
    /// <c>sets.json</c>, <c>cards.jsonl</c>, <c>oracle_tags.jsonl</c>, <c>art_tags.jsonl</c>, <c>rulings.jsonl</c>, <c>card_identifiers.csv</c>
    /// and <c>variants.json</c> (the combos of Commander Spellbook).
    /// A missing file skips its step.
    /// </remarks>
    public string? Directory { get; set; }

    /// <summary>Gets or sets whether the import reads the illustration tags.</summary>
    public bool IncludeIllustrationTags { get; set; } = true;

    /// <summary>Gets or sets whether the import reads the identifiers of MTGJSON.</summary>
    public bool IncludeMtgjsonIdentifiers { get; set; } = true;

    /// <summary>Gets or sets whether the import reads the combos of Commander Spellbook.</summary>
    public bool IncludeCombos { get; set; } = true;

    /// <summary>Gets or sets the address of the Scryfall API.</summary>
    public Uri ScryfallApi { get; set; } = new("https://api.scryfall.com/");

    /// <summary>Gets or sets the address of the MTGJSON files.</summary>
    public Uri MtgjsonApi { get; set; } = new("https://mtgjson.com/api/v5/");

    /// <summary>Gets or sets the address of the bulk file of the combos of Commander Spellbook.</summary>
    public Uri SpellbookCombos { get; set; } = new("https://json.commanderspellbook.com/variants.json.gz");

    /// <summary>Gets or sets the folder for the downloaded files. The default is the temporary folder of the system.</summary>
    public string? DownloadDirectory { get; set; }
}
