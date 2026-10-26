namespace Reliquary.Cards.Import;

/// <summary>A row of <c>reference.oracle_cards</c>, with its faces and its relations.</summary>
internal sealed record OracleRow(
    Guid Id,
    string Name,
    string NameNormalized,
    string Layout,
    string? ManaCost,
    decimal ManaValue,
    string? TypeLine,
    string? OracleText,
    string[] Colors,
    short ColorsMask,
    string[] ColorIdentity,
    short ColorIdentityMask,
    string[]? ColorIndicator,
    string[] ProducedMana,
    short ProducedManaMask,
    string[] Keywords,
    string? Power,
    string? Toughness,
    string? Loyalty,
    string? Defense,
    string? HandModifier,
    string? LifeModifier,
    string Legalities,
    bool Reserved,
    bool GameChanger,
    int? EdhrecRank,
    int? PennyRank,
    IReadOnlyList<FaceRow> Faces);

/// <summary>A row of <c>reference.card_faces</c>.</summary>
internal sealed record FaceRow(
    short Index,
    string Name,
    string? ManaCost,
    decimal ManaValue,
    string? TypeLine,
    string? OracleText,
    string[] Colors,
    short ColorsMask,
    string[]? ColorIndicator,
    string? Power,
    string? Toughness,
    string? Loyalty,
    string? Defense);

/// <summary>A row of <c>reference.card_names</c>.</summary>
internal sealed record NameRow(Guid OracleId, string Name, string NameNormalized, string Lang, string Kind);

/// <summary>A row of <c>reference.card_relations</c>.</summary>
internal sealed record RelationRow(Guid OracleId, Guid RelatedPrintingId, string RelatedName, string Component);

/// <summary>A row of <c>reference.printings</c>, with its faces.</summary>
internal sealed record PrintingRow(
    Guid Id,
    Guid OracleId,
    string SetCode,
    string SetName,
    string SetType,
    string CollectorNumber,
    int? CollectorNumberValue,
    string Lang,
    DateOnly ReleasedAt,
    string Rarity,
    short RarityRank,
    string[] Finishes,
    string[] Games,
    bool Digital,
    bool Promo,
    string[] PromoTypes,
    bool Reprint,
    bool Variation,
    bool Oversized,
    bool FullArt,
    bool Textless,
    bool Booster,
    string? Frame,
    string[] FrameEffects,
    string? BorderColor,
    string? SecurityStamp,
    string? Watermark,
    string? Artist,
    Guid? IllustrationId,
    string? FlavorText,
    string? FlavorName,
    string? PrintedName,
    string? PrintedTypeLine,
    string? PrintedText,
    string? ImageStatus,
    string? ImageUris,
    string Prices,
    int[] MultiverseIds,
    int? MtgoId,
    int? ArenaId,
    int? TcgplayerId,
    int? CardmarketId,
    IReadOnlyList<PrintingFaceRow> Faces);

/// <summary>A row of <c>reference.printing_faces</c>.</summary>
internal sealed record PrintingFaceRow(
    short Index,
    string Name,
    string? PrintedName,
    string? PrintedTypeLine,
    string? PrintedText,
    string? FlavorText,
    string? Artist,
    Guid? IllustrationId,
    string? Watermark,
    string? ImageUris);
