using System.Text.Json.Serialization;

namespace Reliquary.Cards.Import.Spellbook;

/// <summary>
/// The bulk file of Commander Spellbook: <c>variants.json</c>.
/// The class contains only the fields that Reliquary imports.
/// </summary>
/// <remarks>
/// See https://github.com/SpaceCowMedia/commander-spellbook-backend/blob/master/docs/api.md.
/// Each item of <see cref="Variants"/> has the format of the endpoint <c>GET /variants/</c> of the API of Commander Spellbook.
/// Commander Spellbook calls a combo a "variant". Reliquary calls it a combo, because a variant is a different thing in Reliquary.
/// </remarks>
internal sealed class SpellbookFile
{
    public DateTimeOffset? Timestamp { get; set; }

    public string? Version { get; set; }

    public List<SpellbookCombo> Variants { get; set; } = [];
}

/// <summary>
/// One combo of Commander Spellbook.
/// </summary>
internal sealed class SpellbookCombo
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the status. The bulk file contains the public combos: <c>OK</c> and <c>E</c> (example).</summary>
    public string? Status { get; set; }

    public List<SpellbookCardUse> Uses { get; set; } = [];

    /// <summary>Gets or sets the templates: requirements such as "a creature with a sacrifice ability", not a named card.</summary>
    public List<SpellbookTemplateUse> Requires { get; set; } = [];

    public List<SpellbookProduces> Produces { get; set; } = [];

    public string? ManaNeeded { get; set; }

    public int? ManaValueNeeded { get; set; }

    public int? Popularity { get; set; }
}

/// <summary>
/// One card of a combo.
/// </summary>
internal sealed class SpellbookCardUse
{
    public SpellbookCard Card { get; set; } = new();

    public int Quantity { get; set; } = 1;
}

/// <summary>
/// A card of Commander Spellbook. The Oracle ID is the Oracle ID of Scryfall.
/// </summary>
internal sealed class SpellbookCard
{
    public string Name { get; set; } = string.Empty;

    public Guid? OracleId { get; set; }
}

/// <summary>
/// One template of a combo.
/// </summary>
internal sealed class SpellbookTemplateUse
{
    public SpellbookTemplate? Template { get; set; }
}

/// <summary>
/// A template of Commander Spellbook.
/// </summary>
internal sealed class SpellbookTemplate
{
    public string? Name { get; set; }
}

/// <summary>
/// One result of a combo.
/// </summary>
internal sealed class SpellbookProduces
{
    public SpellbookFeature Feature { get; set; } = new();
}

/// <summary>
/// A result ("feature") of Commander Spellbook.
/// </summary>
internal sealed class SpellbookFeature
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the status: <c>S</c> (standalone), <c>C</c> (contextual), <c>H</c> (helper) or a utility status.</summary>
    public string? Status { get; set; }
}

/// <summary>
/// The JSON settings for the Commander Spellbook objects: camel case names.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(SpellbookFile))]
internal sealed partial class SpellbookJson : JsonSerializerContext;
