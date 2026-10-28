using System.Globalization;
using Reliquary.BuildingBlocks.Collection;

namespace Reliquary.Collection.Exchange;

/// <summary>
/// Reads and writes the values of the card fields in the files of the other tools: languages, finishes, conditions, prices and dates.
/// </summary>
internal static class FileValues
{
    /// <summary>The English names of the languages, by language code.</summary>
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.Ordinal)
    {
        ["en"] = "English",
        ["es"] = "Spanish",
        ["fr"] = "French",
        ["de"] = "German",
        ["it"] = "Italian",
        ["pt"] = "Portuguese",
        ["ja"] = "Japanese",
        ["ko"] = "Korean",
        ["ru"] = "Russian",
        ["zhs"] = "Chinese Simplified",
        ["zht"] = "Chinese Traditional",
        ["he"] = "Hebrew",
        ["la"] = "Latin",
        ["grc"] = "Ancient Greek",
        ["ar"] = "Arabic",
        ["sa"] = "Sanskrit",
        ["ph"] = "Phyrexian",
        ["qya"] = "Quenya",
    };

    /// <summary>The other names of the languages in the files, without spaces and in lowercase.</summary>
    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.Ordinal)
    {
        ["jp"] = "ja",
        ["kr"] = "ko",
        ["cn"] = "zhs",
        ["tw"] = "zht",
        ["zh"] = "zhs",
        ["zhcn"] = "zhs",
        ["zhtw"] = "zht",
        ["zhhans"] = "zhs",
        ["zhhant"] = "zht",
        ["simplifiedchinese"] = "zhs",
        ["traditionalchinese"] = "zht",
        ["chinesesimplified"] = "zhs",
        ["chinesetraditional"] = "zht",
        ["portuguesebrazil"] = "pt",
        ["brazilianportuguese"] = "pt",
    };

    /// <summary>The names of the conditions in the files, without spaces and in lowercase.</summary>
    private static readonly Dictionary<string, string> ConditionNames = new(StringComparer.Ordinal)
    {
        ["mint"] = CardConditions.Mint,
        ["m"] = CardConditions.Mint,
        ["mt"] = CardConditions.Mint,
        ["nearmint"] = CardConditions.NearMint,
        ["nm"] = CardConditions.NearMint,
        ["excellent"] = CardConditions.Excellent,
        ["ex"] = CardConditions.Excellent,
        ["good"] = CardConditions.Good,
        ["gd"] = CardConditions.Good,
        ["goodlightlyplayed"] = CardConditions.Good,
        ["lightplayed"] = CardConditions.LightPlayed,
        ["lightlyplayed"] = CardConditions.LightPlayed,
        ["lp"] = CardConditions.LightPlayed,
        ["played"] = CardConditions.Played,
        ["pl"] = CardConditions.Played,
        ["moderatelyplayed"] = CardConditions.Played,
        ["mp"] = CardConditions.Played,
        ["heavilyplayed"] = CardConditions.Played,
        ["hp"] = CardConditions.Played,
        ["poor"] = CardConditions.Poor,
        ["po"] = CardConditions.Poor,
        ["damaged"] = CardConditions.Poor,
        ["dmg"] = CardConditions.Poor,
    };

    /// <summary>
    /// Reads a language: a language code, for example <c>fr</c>, or an English name, for example <c>French</c>.
    /// </summary>
    /// <param name="value">The value of the file.</param>
    /// <param name="language">The language code, or <see langword="null"/> for an empty value.</param>
    /// <returns><see langword="false"/> when the value is not a known language.</returns>
    public static bool TryReadLanguage(string? value, out string? language)
    {
        language = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var key = Letters(value);
        if (CollectionRules.Languages.Contains(key))
        {
            language = key;
            return true;
        }

        language = LanguageNames.FirstOrDefault(pair => Letters(pair.Value) == key).Key ?? LanguageAliases.GetValueOrDefault(key);
        return language is not null;
    }

    /// <summary>Gets the English name of a language code, for example <c>French</c>.</summary>
    public static string LanguageName(string language) => LanguageNames.GetValueOrDefault(language, language);

    /// <summary>
    /// Reads a finish. <c>foil</c>, <c>true</c>, <c>yes</c> and <c>1</c> give a foil card. <c>etched</c> gives an etched card.
    /// An empty value gives <see langword="null"/>. A different value gives a nonfoil card.
    /// </summary>
    public static string? ReadFinish(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Letters(value) switch
        {
            "foil" or "true" or "yes" or "1" or "f" => "foil",
            "etched" or "etchedfoil" or "e" => "etched",
            _ => "nonfoil",
        };
    }

    /// <summary>
    /// Reads a condition of the scale of Cardmarket, of the scale of TCGplayer, or of the scale of Deckbox. See docs/architecture/collection.md.
    /// </summary>
    /// <param name="value">The value of the file.</param>
    /// <param name="condition">The condition, or <see langword="null"/> for an empty value.</param>
    /// <returns><see langword="false"/> when the value is not a known condition.</returns>
    public static bool TryReadCondition(string? value, out string? condition)
    {
        condition = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        condition = ConditionNames.GetValueOrDefault(Letters(value));
        return condition is not null;
    }

    /// <summary>
    /// Reads a price, for example <c>1.50</c>, <c>1,50</c>, <c>$1.50</c> or <c>1.50 €</c>.
    /// The symbols <c>$</c>, <c>€</c> and <c>£</c> give the currency.
    /// </summary>
    /// <param name="value">The value of the file.</param>
    /// <param name="price">The price, rounded to 2 decimals, or <see langword="null"/> for an empty value.</param>
    /// <param name="currency">The currency of the symbol, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the value is not a price from 0 to the maximum price.</returns>
    public static bool TryReadPrice(string? value, out decimal? price, out string? currency)
    {
        price = null;
        currency = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var text = value.Trim();
        foreach (var (symbol, code) in new[] { ("$", "USD"), ("€", "EUR"), ("£", "GBP") })
        {
            if (text.Contains(symbol, StringComparison.Ordinal))
            {
                currency = code;
                text = text.Replace(symbol, string.Empty, StringComparison.Ordinal).Trim();
            }
        }

        if (!text.Contains('.', StringComparison.Ordinal) && text.Count(character => character == ',') == 1)
        {
            text = text.Replace(',', '.');
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number > CollectionRules.MaxPrice)
        {
            return false;
        }

        price = decimal.Round(number, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// Reads a currency code of 3 letters, for example <c>EUR</c>.
    /// </summary>
    /// <returns>The code in uppercase, or <see langword="null"/> when the value is not a code of 3 letters.</returns>
    public static string? ReadCurrency(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length == 3 && text.All(char.IsAsciiLetter) ? text.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Reads a date: <c>2026-10-28</c>, a date and time that starts with this date, or <c>10/28/2026</c>.
    /// </summary>
    /// <param name="value">The value of the file.</param>
    /// <param name="date">The date, or <see langword="null"/> for an empty value.</param>
    /// <returns><see langword="false"/> when the value is not a date.</returns>
    public static bool TryReadDate(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var text = value.Trim();
        if (text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            date = iso;
            return true;
        }

        if (DateOnly.TryParseExact(text, ["M/d/yyyy", "MM/dd/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var american))
        {
            date = american;
            return true;
        }

        return false;
    }

    /// <summary>Writes a price with 2 decimals, for example <c>1.50</c>, or an empty text.</summary>
    public static string Price(decimal? price) => price?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Writes a date, for example <c>2026-10-28</c>, or an empty text.</summary>
    public static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Writes a quantity.</summary>
    public static string Quantity(int quantity) => quantity.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the letters and the digits of a value, in lowercase.</summary>
    private static string Letters(string value) =>
        new([.. value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
