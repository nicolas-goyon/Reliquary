using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Reliquary.Cards.Import.Scryfall;
using Reliquary.Cards.Text;

namespace Reliquary.Cards.Import;

/// <summary>
/// Converts Scryfall card objects into the rows of the reference tables.
/// </summary>
internal static class CardMapper
{
    /// <summary>The layouts of the cards that are not game cards: tokens, emblems, art cards, planes, schemes and vanguards.</summary>
    public static readonly IReadOnlySet<string> ExtraLayouts = new HashSet<string>(StringComparer.Ordinal)
    {
        "token", "double_faced_token", "emblem", "art_series", "planar", "scheme", "vanguard",
    };

    private static readonly JsonSerializerOptions HashJson = new() { WriteIndented = false };

    private static readonly IReadOnlyDictionary<string, short> RarityRanks = new Dictionary<string, short>(StringComparer.Ordinal)
    {
        ["common"] = 0,
        ["uncommon"] = 1,
        ["rare"] = 2,
        ["special"] = 3,
        ["mythic"] = 4,
        ["bonus"] = 5,
    };

    /// <summary>
    /// Gets the Oracle identifier of a card object. A reversible card has its identifier on its faces.
    /// </summary>
    /// <param name="card">The card object.</param>
    /// <returns>The identifier, or <see langword="null"/> when the object has none.</returns>
    public static Guid? OracleIdOf(ScryfallCard card) => card.OracleId ?? card.CardFaces?.FirstOrDefault()?.OracleId;

    /// <summary>
    /// Creates the Oracle row of a card object.
    /// </summary>
    /// <param name="card">The card object.</param>
    /// <param name="oracleId">The Oracle identifier of the card.</param>
    /// <returns>The Oracle row.</returns>
    public static OracleRow ToOracle(ScryfallCard card, Guid oracleId)
    {
        // A reversible card shows the same card on its two sides. The first face is the Oracle card.
        if (card.Layout == "reversible_card" && card.CardFaces is [var front, ..])
        {
            var faceRow = Face(0, front, front.Cmc ?? ManaCost.Value(front.ManaCost));
            return new OracleRow(
                oracleId,
                front.Name,
                NameNormalizer.Normalize(front.Name),
                card.Layout,
                Empty(front.ManaCost),
                front.Cmc ?? ManaCost.Value(front.ManaCost),
                front.TypeLine,
                front.OracleText,
                Sorted(front.Colors),
                ManaCost.Mask(front.Colors),
                Sorted(card.ColorIdentity),
                ManaCost.Mask(card.ColorIdentity),
                front.ColorIndicator is null ? null : Sorted(front.ColorIndicator),
                Sorted(card.ProducedMana),
                ManaCost.Mask(card.ProducedMana),
                [.. card.Keywords ?? []],
                front.Power,
                front.Toughness,
                front.Loyalty,
                front.Defense,
                card.HandModifier,
                card.LifeModifier,
                Legalities(card.Legalities),
                card.Reserved,
                card.GameChanger,
                card.EdhrecRank,
                card.PennyRank,
                [faceRow]);
        }

        var faces = card.CardFaces is { Count: > 0 } cardFaces
            ? cardFaces.Select((face, index) => Face((short)index, face, face.Cmc ?? ManaCost.Value(face.ManaCost))).ToList()
            : [new FaceRow(0, card.Name, Empty(card.ManaCost), card.Cmc ?? 0, card.TypeLine, card.OracleText, Sorted(card.Colors), ManaCost.Mask(card.Colors), card.ColorIndicator is null ? null : Sorted(card.ColorIndicator), card.Power, card.Toughness, card.Loyalty, card.Defense)];

        var colors = card.Colors ?? faces.SelectMany(face => face.Colors).Distinct(StringComparer.Ordinal).ToList();
        var front0 = faces[0];

        return new OracleRow(
            oracleId,
            card.Name,
            NameNormalizer.Normalize(card.Name),
            card.Layout,
            Empty(card.ManaCost) ?? JoinCosts(faces),
            card.Cmc ?? front0.ManaValue,
            card.TypeLine ?? string.Join(" // ", faces.Select(face => face.TypeLine)),
            card.OracleText ?? JoinTexts(faces),
            Sorted(colors),
            ManaCost.Mask(colors),
            Sorted(card.ColorIdentity),
            ManaCost.Mask(card.ColorIdentity),
            card.ColorIndicator is null ? null : Sorted(card.ColorIndicator),
            Sorted(card.ProducedMana),
            ManaCost.Mask(card.ProducedMana),
            [.. card.Keywords ?? []],
            card.Power ?? front0.Power,
            card.Toughness ?? front0.Toughness,
            card.Loyalty ?? front0.Loyalty,
            card.Defense ?? front0.Defense,
            card.HandModifier,
            card.LifeModifier,
            Legalities(card.Legalities),
            card.Reserved,
            card.GameChanger,
            card.EdhrecRank,
            card.PennyRank,
            faces);
    }

    /// <summary>
    /// Gets the names of a card object: Oracle name, face names, localized names and alternate names.
    /// </summary>
    /// <param name="card">The card object.</param>
    /// <param name="oracle">The Oracle row of the card.</param>
    /// <returns>The names.</returns>
    public static IEnumerable<NameRow> Names(ScryfallCard card, OracleRow oracle)
    {
        yield return Name(oracle.Id, oracle.Name, "en", "oracle");

        if (oracle.Faces.Count > 1)
        {
            foreach (var face in oracle.Faces)
            {
                yield return Name(oracle.Id, face.Name, "en", "face");
            }
        }

        if (!string.IsNullOrWhiteSpace(card.PrintedName) && card.PrintedName != card.Name)
        {
            yield return Name(oracle.Id, card.PrintedName, card.Lang, "printed");
        }

        if (!string.IsNullOrWhiteSpace(card.FlavorName))
        {
            yield return Name(oracle.Id, card.FlavorName, card.Lang, "flavor");
        }

        foreach (var face in card.CardFaces ?? [])
        {
            if (!string.IsNullOrWhiteSpace(face.PrintedName) && face.PrintedName != face.Name)
            {
                yield return Name(oracle.Id, face.PrintedName, card.Lang, "printed");
            }

            if (!string.IsNullOrWhiteSpace(face.FlavorName))
            {
                yield return Name(oracle.Id, face.FlavorName, card.Lang, "flavor");
            }
        }
    }

    /// <summary>
    /// Gets the related cards of a card object: tokens, meld parts and combo pieces. The card itself is not in the list.
    /// </summary>
    /// <param name="card">The card object.</param>
    /// <param name="oracleId">The Oracle identifier of the card.</param>
    /// <returns>The relations.</returns>
    public static IEnumerable<RelationRow> Relations(ScryfallCard card, Guid oracleId) =>
        (card.AllParts ?? [])
            .Where(part => part.Id != card.Id)
            .Select(part => new RelationRow(oracleId, part.Id, part.Name, part.Component));

    /// <summary>
    /// Creates the printing row of a card object.
    /// </summary>
    /// <param name="card">The card object.</param>
    /// <param name="oracleId">The Oracle identifier of the card.</param>
    /// <returns>The printing row.</returns>
    public static PrintingRow ToPrinting(ScryfallCard card, Guid oracleId)
    {
        var imageUris = card.ImageUris ?? card.CardFaces?.FirstOrDefault()?.ImageUris;
        var faces = (card.CardFaces ?? [])
            .Select((face, index) => new PrintingFaceRow(
                (short)index,
                face.Name,
                face.PrintedName,
                face.PrintedTypeLine,
                face.PrintedText,
                face.FlavorText,
                face.Artist,
                face.IllustrationId,
                face.Watermark,
                face.ImageUris is null ? null : Json(face.ImageUris)))
            .ToList();

        return new PrintingRow(
            card.Id,
            oracleId,
            card.Set,
            card.SetName,
            card.SetType,
            card.CollectorNumber,
            CollectorNumberValue(card.CollectorNumber),
            card.Lang,
            card.ReleasedAt,
            card.Rarity,
            RarityRanks.GetValueOrDefault(card.Rarity, (short)3),
            [.. card.Finishes ?? []],
            [.. card.Games ?? []],
            card.Digital,
            card.Promo,
            [.. card.PromoTypes ?? []],
            card.Reprint,
            card.Variation,
            card.Oversized,
            card.FullArt,
            card.Textless,
            card.Booster,
            card.StorySpotlight,
            card.ContentWarning,
            card.Frame,
            [.. card.FrameEffects ?? []],
            card.BorderColor,
            card.SecurityStamp,
            card.Watermark ?? card.CardFaces?.FirstOrDefault()?.Watermark,
            card.Artist,
            card.IllustrationId ?? card.CardFaces?.FirstOrDefault()?.IllustrationId,
            card.FlavorText ?? card.CardFaces?.FirstOrDefault()?.FlavorText,
            card.FlavorName,
            card.PrintedName,
            card.PrintedTypeLine,
            card.PrintedText,
            card.ImageStatus,
            imageUris is null ? null : Json(imageUris),
            Json((card.Prices ?? []).Where(price => price.Value is not null).ToDictionary(price => price.Key, price => price.Value)),
            [.. card.MultiverseIds ?? []],
            card.MtgoId,
            card.ArenaId,
            card.TcgplayerId,
            card.CardmarketId,
            faces);
    }

    /// <summary>
    /// Gets a hash of a row, so the import changes only the rows that changed.
    /// </summary>
    /// <typeparam name="T">The type of the row.</typeparam>
    /// <param name="row">The row.</param>
    /// <returns>The SHA-256 hash of the JSON form of the row.</returns>
    public static byte[] Hash<T>(T row) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row, HashJson));

    /// <summary>
    /// Gets the number at the start of a collector number, for example 123 for "123a".
    /// </summary>
    /// <param name="collectorNumber">The collector number.</param>
    /// <returns>The number, or <see langword="null"/> when the collector number does not start with a digit.</returns>
    public static int? CollectorNumberValue(string collectorNumber)
    {
        var digits = new string([.. collectorNumber.TakeWhile(char.IsAsciiDigit).Take(9)]);
        return digits.Length > 0 ? int.Parse(digits, CultureInfo.InvariantCulture) : null;
    }

    private static FaceRow Face(short index, ScryfallFace face, decimal manaValue)
    {
        // Scryfall gives no colors for the halves of split cards, Adventures and prepared creatures.
        var colors = face.Colors ?? face.ColorIndicator ?? ManaCost.Colors(face.ManaCost);
        return new(
            index,
            face.Name,
            Empty(face.ManaCost),
            manaValue,
            face.TypeLine,
            face.OracleText,
            Sorted(colors),
            ManaCost.Mask(colors),
            face.ColorIndicator is null ? null : Sorted(face.ColorIndicator),
            face.Power,
            face.Toughness,
            face.Loyalty,
            face.Defense);
    }

    private static NameRow Name(Guid oracleId, string name, string lang, string kind) =>
        new(oracleId, name, NameNormalizer.Normalize(name), lang, kind);

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? JoinCosts(IReadOnlyList<FaceRow> faces)
    {
        var costs = faces.Select(face => face.ManaCost).ToList();
        if (costs.All(cost => cost is null))
        {
            return null;
        }

        // A back face without a cost (transform, flip) adds nothing to the cost of the card.
        return costs.Skip(1).All(cost => cost is null) ? costs[0] : string.Join(" // ", costs.Select(cost => cost ?? string.Empty));
    }

    private static string? JoinTexts(IReadOnlyList<FaceRow> faces) =>
        faces.All(face => face.OracleText is null) ? null : string.Join("\n//\n", faces.Select(face => face.OracleText ?? string.Empty));

    private static string[] Sorted(IEnumerable<string>? colors) =>
        [.. (colors ?? []).Distinct(StringComparer.Ordinal).OrderBy(color => "WUBRGC".IndexOf(color, StringComparison.Ordinal))];

    private static string Legalities(Dictionary<string, string>? legalities) =>
        Json(new SortedDictionary<string, string>(legalities ?? [], StringComparer.Ordinal));

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, HashJson);
}
