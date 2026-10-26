using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reliquary.Cards.Import.Scryfall;

/// <summary>
/// A card object of the Scryfall API and bulk files. The class contains only the fields that Reliquary imports.
/// See https://scryfall.com/docs/api/cards.
/// </summary>
internal sealed class ScryfallCard
{
    public Guid Id { get; set; }

    public Guid? OracleId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Lang { get; set; } = "en";

    public DateOnly ReleasedAt { get; set; }

    public string Layout { get; set; } = "normal";

    public string? ManaCost { get; set; }

    public decimal? Cmc { get; set; }

    public string? TypeLine { get; set; }

    public string? OracleText { get; set; }

    public List<string>? Colors { get; set; }

    public List<string>? ColorIdentity { get; set; }

    public List<string>? ColorIndicator { get; set; }

    public List<string>? Keywords { get; set; }

    public List<string>? ProducedMana { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    public string? Loyalty { get; set; }

    public string? Defense { get; set; }

    public string? HandModifier { get; set; }

    public string? LifeModifier { get; set; }

    public Dictionary<string, string>? Legalities { get; set; }

    public bool Reserved { get; set; }

    public bool GameChanger { get; set; }

    public int? EdhrecRank { get; set; }

    public int? PennyRank { get; set; }

    public List<ScryfallFace>? CardFaces { get; set; }

    public List<ScryfallRelatedCard>? AllParts { get; set; }

    public string Set { get; set; } = string.Empty;

    public string SetName { get; set; } = string.Empty;

    public string SetType { get; set; } = string.Empty;

    public string CollectorNumber { get; set; } = string.Empty;

    public string Rarity { get; set; } = "common";

    public List<string>? Finishes { get; set; }

    public List<string>? Games { get; set; }

    public bool Digital { get; set; }

    public bool Promo { get; set; }

    public List<string>? PromoTypes { get; set; }

    public bool Reprint { get; set; }

    public bool Variation { get; set; }

    public bool Oversized { get; set; }

    public bool FullArt { get; set; }

    public bool Textless { get; set; }

    public bool Booster { get; set; }

    public bool StorySpotlight { get; set; }

    public bool ContentWarning { get; set; }

    public string? Frame { get; set; }

    public List<string>? FrameEffects { get; set; }

    public string? BorderColor { get; set; }

    public string? SecurityStamp { get; set; }

    public string? Watermark { get; set; }

    public string? Artist { get; set; }

    public Guid? IllustrationId { get; set; }

    public string? FlavorText { get; set; }

    public string? FlavorName { get; set; }

    public string? PrintedName { get; set; }

    public string? PrintedTypeLine { get; set; }

    public string? PrintedText { get; set; }

    public string? ImageStatus { get; set; }

    public Dictionary<string, string>? ImageUris { get; set; }

    public Dictionary<string, string?>? Prices { get; set; }

    public List<int>? MultiverseIds { get; set; }

    public int? MtgoId { get; set; }

    public int? ArenaId { get; set; }

    public int? TcgplayerId { get; set; }

    public int? CardmarketId { get; set; }
}

/// <summary>
/// A face of a card object.
/// </summary>
internal sealed class ScryfallFace
{
    public Guid? OracleId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ManaCost { get; set; }

    public decimal? Cmc { get; set; }

    public string? TypeLine { get; set; }

    public string? OracleText { get; set; }

    public List<string>? Colors { get; set; }

    public List<string>? ColorIndicator { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    public string? Loyalty { get; set; }

    public string? Defense { get; set; }

    public string? Artist { get; set; }

    public Guid? IllustrationId { get; set; }

    public string? FlavorText { get; set; }

    public string? FlavorName { get; set; }

    public string? PrintedName { get; set; }

    public string? PrintedTypeLine { get; set; }

    public string? PrintedText { get; set; }

    public string? Watermark { get; set; }

    public Dictionary<string, string>? ImageUris { get; set; }
}

/// <summary>
/// A related card in the <c>all_parts</c> list of a card object.
/// </summary>
internal sealed class ScryfallRelatedCard
{
    public Guid Id { get; set; }

    public string Component { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A tag object of the Oracle tags and illustration tags bulk files.
/// </summary>
internal sealed class ScryfallTag
{
    public Guid Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<Guid>? ParentIds { get; set; }

    public List<string>? Aliases { get; set; }

    public List<ScryfallTagging>? Taggings { get; set; }
}

/// <summary>
/// One link between a tag and a card (Oracle tags) or an illustration (illustration tags).
/// </summary>
internal sealed class ScryfallTagging
{
    public Guid? OracleId { get; set; }

    public Guid? IllustrationId { get; set; }

    public string? Weight { get; set; }
}

/// <summary>
/// A ruling object of the rulings bulk file.
/// </summary>
internal sealed class ScryfallRuling
{
    public Guid OracleId { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateOnly PublishedAt { get; set; }

    public string Comment { get; set; } = string.Empty;
}

/// <summary>
/// A set object of the Scryfall sets API.
/// </summary>
internal sealed class ScryfallSet
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SetType { get; set; } = string.Empty;

    public DateOnly? ReleasedAt { get; set; }

    public string? BlockCode { get; set; }

    public string? Block { get; set; }

    public string? ParentSetCode { get; set; }

    public int CardCount { get; set; }

    public bool Digital { get; set; }

    public string? IconSvgUri { get; set; }
}

/// <summary>
/// A list object of the Scryfall API.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
internal sealed class ScryfallList<T>
{
    public List<T> Data { get; set; } = [];

    public bool HasMore { get; set; }

    public string? NextPage { get; set; }
}

/// <summary>
/// A bulk data object of the Scryfall API: one downloadable file.
/// </summary>
internal sealed class ScryfallBulkFile
{
    public string Type { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public string? DownloadUri { get; set; }

    public string? JsonlDownloadUri { get; set; }
}

/// <summary>
/// The JSON settings for the Scryfall objects: snake case names.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ScryfallCard))]
[JsonSerializable(typeof(ScryfallTag))]
[JsonSerializable(typeof(ScryfallRuling))]
[JsonSerializable(typeof(ScryfallList<ScryfallSet>))]
[JsonSerializable(typeof(ScryfallList<ScryfallBulkFile>))]
[JsonSerializable(typeof(ScryfallList<ScryfallCard>))]
internal sealed partial class ScryfallJson : JsonSerializerContext
{
    /// <summary>
    /// Reads one object from a line of a JSONL file.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="line">The line.</param>
    /// <param name="typeInfo">The JSON type information.</param>
    /// <returns>The object.</returns>
    public static T Read<T>(string line, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(line, typeInfo) ?? throw new JsonException("The line contains no object.");
}
