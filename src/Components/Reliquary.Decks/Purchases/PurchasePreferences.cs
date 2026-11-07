using System.Text.Json;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Collection;

namespace Reliquary.Decks.Purchases;

/// <summary>
/// The price limit of a purchase.
/// </summary>
/// <param name="Amount">The amount.</param>
/// <param name="Currency">The ISO 4217 currency code, for example <c>EUR</c> or <c>USD</c>.</param>
public sealed record PriceLimit(decimal Amount, string Currency);

/// <summary>
/// The purchase preferences of a deck line: which copies of the card the user wants for the deck.
/// See docs/architecture/purchase-list.md.
/// </summary>
/// <param name="WantedPrintingIds">The printings that the user wants. An empty list accepts each printing that is not avoided.</param>
/// <param name="AvoidedPrintingIds">The printings that the user does not want.</param>
/// <param name="Languages">The languages that the user accepts, for example <c>fr</c> and <c>en</c>. An empty list accepts each language.</param>
/// <param name="MinimumCondition">The worst condition that the user accepts, or <see langword="null"/>. See <see cref="CardConditions"/>.</param>
/// <param name="Foil"><c>any</c>, <c>foil</c> or <c>nonfoil</c>.</param>
/// <param name="MaximumPrice">The highest price of a purchase, or <see langword="null"/>.</param>
public sealed record PurchasePreferences(
    IReadOnlyList<Guid>? WantedPrintingIds = null,
    IReadOnlyList<Guid>? AvoidedPrintingIds = null,
    IReadOnlyList<string>? Languages = null,
    string? MinimumCondition = null,
    string? Foil = null,
    PriceLimit? MaximumPrice = null)
{
    /// <summary>The values of the foil preference.</summary>
    public static readonly IReadOnlyList<string> FoilValues = ["any", "foil", "nonfoil"];

    /// <summary>The maximum number of printings or languages in one list.</summary>
    public const int MaxItems = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Gets the preferences with no limit.</summary>
    public static PurchasePreferences None { get; } = new();

    /// <summary>
    /// Converts the preferences to the JSON text of the database, or <see langword="null"/> for no preference.
    /// </summary>
    public static string? ToJson(PurchasePreferences? preferences) =>
        preferences is null || preferences.IsEmpty ? null : JsonSerializer.Serialize(preferences.Normalize(), Json);

    /// <summary>
    /// Reads the JSON text of the database. A null text gives <see cref="None"/>.
    /// </summary>
    public static PurchasePreferences FromJson(string? json) =>
        string.IsNullOrEmpty(json) ? None : JsonSerializer.Deserialize<PurchasePreferences>(json, Json) ?? None;

    /// <summary>
    /// Reads the JSON text of the database for a response. A null text gives <see langword="null"/>.
    /// </summary>
    public static PurchasePreferences? ReadOrNull(string? json) =>
        string.IsNullOrEmpty(json) ? null : FromJson(json);

    /// <summary>Gets a value that tells whether the preferences accept each copy.</summary>
    public bool IsEmpty =>
        (WantedPrintingIds is null || WantedPrintingIds.Count == 0)
        && (AvoidedPrintingIds is null || AvoidedPrintingIds.Count == 0)
        && (Languages is null || Languages.Count == 0)
        && MinimumCondition is null
        && (Foil is null or "any")
        && MaximumPrice is null;

    /// <summary>
    /// Checks the preferences of a request.
    /// </summary>
    /// <param name="field">The name of the field in the request, for the errors.</param>
    /// <returns>The errors. An empty list means that the preferences are valid.</returns>
    public IReadOnlyList<FieldError> Validate(string field)
    {
        var errors = new List<FieldError>();
        if ((WantedPrintingIds?.Count ?? 0) > MaxItems || (AvoidedPrintingIds?.Count ?? 0) > MaxItems || (Languages?.Count ?? 0) > MaxItems)
        {
            errors.Add(new FieldError(field, FieldErrorCodes.TooLong, $"A list of the purchase preferences can have {MaxItems} items or fewer."));
        }

        if (MinimumCondition is not null && !CardConditions.All.Contains(MinimumCondition))
        {
            errors.Add(new FieldError($"{field}.minimumCondition", FieldErrorCodes.NotAllowed, $"Use one of these conditions: {string.Join(", ", CardConditions.All)}."));
        }

        if (Foil is not null && !FoilValues.Contains(Foil))
        {
            errors.Add(new FieldError($"{field}.foil", FieldErrorCodes.NotAllowed, "Use any, foil or nonfoil."));
        }

        if (MaximumPrice is { } price && (price.Amount <= 0 || price.Currency.Length != 3 || !price.Currency.All(char.IsAsciiLetterUpper)))
        {
            errors.Add(new FieldError($"{field}.maximumPrice", FieldErrorCodes.InvalidFormat, "Give an amount more than 0 and a currency code of 3 capital letters, for example EUR."));
        }

        if (WantedPrintingIds is not null && AvoidedPrintingIds is not null && WantedPrintingIds.Intersect(AvoidedPrintingIds).Any())
        {
            errors.Add(new FieldError($"{field}.avoidedPrintingIds", FieldErrorCodes.NotAllowed, "A printing cannot be wanted and avoided."));
        }

        return errors;
    }

    /// <summary>Gives a stable form: sorted lists with no duplicates, and lowercase languages.</summary>
    public PurchasePreferences Normalize() => this with
    {
        WantedPrintingIds = WantedPrintingIds is null ? null : [.. WantedPrintingIds.Distinct().Order()],
        AvoidedPrintingIds = AvoidedPrintingIds is null ? null : [.. AvoidedPrintingIds.Distinct().Order()],
        Languages = Languages is null ? null : [.. Languages.Select(language => language.Trim().ToLowerInvariant()).Where(language => language.Length > 0).Distinct().Order(StringComparer.Ordinal)],
    };
}
