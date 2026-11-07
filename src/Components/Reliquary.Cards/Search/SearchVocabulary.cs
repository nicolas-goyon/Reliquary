using System.Collections.Frozen;

namespace Reliquary.Cards.Search;

/// <summary>
/// The values that the keywords of the search accept: colors, formats, languages, rarities, set types and others.
/// </summary>
internal static class SearchVocabulary
{
    /// <summary>The bit of each color letter, as in the color masks of the database: W=1, U=2, B=4, R=8, G=16, C=32.</summary>
    public static readonly FrozenDictionary<char, int> ColorBits = new Dictionary<char, int>
    {
        ['w'] = 1,
        ['u'] = 2,
        ['b'] = 4,
        ['r'] = 8,
        ['g'] = 16,
        ['c'] = 32,
    }.ToFrozenDictionary();

    /// <summary>The names and the nicknames of the color sets: colors, guilds, shards, wedges, colleges and four-color names.</summary>
    public static readonly FrozenDictionary<string, string> ColorNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["white"] = "w",
        ["blue"] = "u",
        ["black"] = "b",
        ["red"] = "r",
        ["green"] = "g",
        ["azorius"] = "wu",
        ["dimir"] = "ub",
        ["rakdos"] = "br",
        ["gruul"] = "rg",
        ["selesnya"] = "gw",
        ["orzhov"] = "wb",
        ["izzet"] = "ur",
        ["golgari"] = "bg",
        ["boros"] = "rw",
        ["simic"] = "gu",
        ["bant"] = "gwu",
        ["esper"] = "wub",
        ["grixis"] = "ubr",
        ["jund"] = "brg",
        ["naya"] = "rgw",
        ["abzan"] = "wbg",
        ["jeskai"] = "urw",
        ["sultai"] = "bgu",
        ["mardu"] = "rwb",
        ["temur"] = "gur",
        ["silverquill"] = "wb",
        ["prismari"] = "ur",
        ["witherbloom"] = "bg",
        ["lorehold"] = "rw",
        ["quandrix"] = "gu",
        ["chaos"] = "ubrg",
        ["aggression"] = "wbrg",
        ["altruism"] = "wurg",
        ["growth"] = "wubg",
        ["artifice"] = "wubr",
        ["rainbow"] = "wubrg",
        ["fivecolor"] = "wubrg",
    }.ToFrozenDictionary();

    /// <summary>The formats of the legalities, by the names that the query can use.</summary>
    public static readonly FrozenDictionary<string, string> Formats = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["standard"] = "standard",
        ["future"] = "future",
        ["historic"] = "historic",
        ["timeless"] = "timeless",
        ["gladiator"] = "gladiator",
        ["pioneer"] = "pioneer",
        ["modern"] = "modern",
        ["legacy"] = "legacy",
        ["pauper"] = "pauper",
        ["vintage"] = "vintage",
        ["penny"] = "penny",
        ["pennydreadful"] = "penny",
        ["commander"] = "commander",
        ["edh"] = "commander",
        ["oathbreaker"] = "oathbreaker",
        ["standardbrawl"] = "standardbrawl",
        ["brawl"] = "brawl",
        ["historicbrawl"] = "brawl",
        ["competitivebrawl"] = "competitivebrawl",
        ["alchemy"] = "alchemy",
        ["paupercommander"] = "paupercommander",
        ["pdh"] = "paupercommander",
        ["duel"] = "duel",
        ["duelcommander"] = "duel",
        ["oldschool"] = "oldschool",
        ["premodern"] = "premodern",
        ["predh"] = "predh",
        ["tlr"] = "tlr",
    }.ToFrozenDictionary();

    /// <summary>The languages of the printings, by code and by name.</summary>
    public static readonly FrozenDictionary<string, string> Languages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en"] = "en",
        ["english"] = "en",
        ["es"] = "es",
        ["sp"] = "es",
        ["spanish"] = "es",
        ["fr"] = "fr",
        ["french"] = "fr",
        ["de"] = "de",
        ["german"] = "de",
        ["it"] = "it",
        ["italian"] = "it",
        ["pt"] = "pt",
        ["portuguese"] = "pt",
        ["ja"] = "ja",
        ["jp"] = "ja",
        ["japanese"] = "ja",
        ["ko"] = "ko",
        ["kr"] = "ko",
        ["korean"] = "ko",
        ["ru"] = "ru",
        ["russian"] = "ru",
        ["zhs"] = "zhs",
        ["cs"] = "zhs",
        ["chinese"] = "zhs",
        ["simplifiedchinese"] = "zhs",
        ["zht"] = "zht",
        ["ct"] = "zht",
        ["traditionalchinese"] = "zht",
        ["he"] = "he",
        ["hebrew"] = "he",
        ["la"] = "la",
        ["latin"] = "la",
        ["grc"] = "grc",
        ["ancientgreek"] = "grc",
        ["ar"] = "ar",
        ["arabic"] = "ar",
        ["sa"] = "sa",
        ["sanskrit"] = "sa",
        ["ph"] = "ph",
        ["phyrexian"] = "ph",
        ["qya"] = "qya",
        ["quenya"] = "qya",
        ["dw"] = "dw",
    }.ToFrozenDictionary();

    /// <summary>The rarities and their rank in the column <c>rarity_rank</c>.</summary>
    public static readonly FrozenDictionary<string, short> Rarities = new Dictionary<string, short>(StringComparer.Ordinal)
    {
        ["c"] = 0,
        ["common"] = 0,
        ["u"] = 1,
        ["uncommon"] = 1,
        ["r"] = 2,
        ["rare"] = 2,
        ["s"] = 3,
        ["special"] = 3,
        ["m"] = 4,
        ["mythic"] = 4,
        ["b"] = 5,
        ["bonus"] = 5,
    }.ToFrozenDictionary();

    /// <summary>The set types of Scryfall. The query can omit the underscores, for example <c>st:draftinnovation</c>.</summary>
    public static readonly FrozenDictionary<string, string> SetTypes = new[]
    {
        "core", "expansion", "masters", "eternal", "alchemy", "masterpiece", "arsenal", "from_the_vault", "spellbook", "premium_deck",
        "duel_deck", "draft_innovation", "treasure_chest", "commander", "planechase", "archenemy", "vanguard", "funny", "starter",
        "box", "promo", "token", "memorabilia", "minigame",
    }.ToFrozenDictionary(type => type.Replace("_", string.Empty, StringComparison.Ordinal), StringComparer.Ordinal);

    /// <summary>The games of the printings.</summary>
    public static readonly FrozenSet<string> Games = new[] { "paper", "mtgo", "arena", "astral", "sega" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The frame editions. Other values of <c>frame:</c> are frame effects.</summary>
    public static readonly FrozenSet<string> FrameEditions = new[] { "1993", "1997", "2003", "2015", "future" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The frame effects that <c>is:</c> accepts directly, for example <c>is:colorshifted</c>.</summary>
    public static readonly FrozenSet<string> FrameEffects = new[]
    {
        "legendary", "miracle", "enchantment", "draft", "devoid", "lesson", "snow", "shatteredglass", "colorshifted", "tombstone",
        "showcase", "extendedart", "inverted", "companion", "spree", "fandfc", "sunmoondfc", "compasslanddfc", "originpwdfc",
        "mooneldrazidfc", "waxingandwaningmoondfc", "convertdfc", "upsidedowndfc", "nyxtouched", "nyxborn",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The promo types of Scryfall that <c>is:</c> accepts, for example <c>is:prerelease</c>. The query can use underscores.</summary>
    public static readonly FrozenSet<string> PromoTypes = new[]
    {
        "alchemy", "arenaleague", "beginnerbox", "boosterfun", "boxtopper", "brawldeck", "bringafriend", "bundle", "buyabox",
        "commanderparty", "commanderpromo", "concept", "confettifoil", "convention", "cosmicfoil", "datestamped", "dazzlefoil",
        "dossier", "doubleexposure", "doublerainbow", "draculaseries", "draftweekend", "dragonscalefoil", "duels", "embossed",
        "event", "facetfoil", "firstplacefoil", "fnm", "fracturefoil", "galaxyfoil", "gameday", "giftbox", "gilded", "gleaminggold",
        "glossy", "godzillaseries", "halofoil", "headliner", "imagine", "instore", "intropack", "invisibleink", "japanshowcase",
        "jpwalker", "judgegift", "league", "magnified", "manafoil", "mediainsert", "metal", "moonlitland", "neonink", "oilslick",
        "openhouse", "planeswalkerdeck", "plastic", "playerrewards", "playpromo", "playtest", "portrait", "poster", "premiereshop",
        "prerelease", "promopack", "rainbowfoil", "raisedfoil", "ravnicacity", "rebalanced", "release", "resale", "ripplefoil",
        "schinesealtart", "scroll", "serialized", "setextension", "setpromo", "silverfoil", "silverscroll", "singularityfoil",
        "sldbonus", "sourcematerial", "stamped", "standardshowdown", "startercollection", "starterdeck", "stepandcompleat",
        "storechampionship", "surgefoil", "textured", "themepack", "thick", "tourney", "universesbeyond", "upsidedown",
        "upsidedownback", "vault", "wizardsplaynetwork",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The types of the extra cards. A positive <c>t:</c> term with one of these values shows the extra cards.</summary>
    public static readonly FrozenSet<string> ExtraTypes = new[]
    {
        "vanguard", "plane", "phenomenon", "scheme", "token", "emblem", "dungeon", "card", "hero", "stickers", "attraction",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The card types and the supertypes. A <c>t:</c> term with one of these words matches only the full word,
    /// so <c>t:plane</c> does not find "Planet" or "Planeswalker".
    /// </summary>
    public static readonly FrozenSet<string> TypeWords = new[]
    {
        "artifact", "battle", "conspiracy", "creature", "dungeon", "emblem", "enchantment", "hero", "instant", "kindred", "land",
        "phenomenon", "plane", "planeswalker", "scheme", "sorcery", "tribal", "vanguard", "basic", "legendary", "ongoing", "snow",
        "world", "elite", "host", "token", "plan",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The layouts of the cards that have no colors of their own in the Scryfall data. Each face has its colors.</summary>
    public static readonly string[] FaceColorLayouts = ["transform", "modal_dfc", "reversible_card", "double_faced_token"];

    /// <summary>The canonical hybrid symbols. The compiler changes <c>{B/U}</c> into <c>{U/B}</c>.</summary>
    public static readonly FrozenSet<string> HybridPairs = new[]
    {
        "W/U", "U/B", "B/R", "R/G", "G/W", "W/B", "U/R", "B/G", "R/W", "G/U",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The words that the Oracle text uses when a card mentions itself, for example "this creature".</summary>
    public static readonly string SelfReferencePattern =
        @"\mthis (spell|card|creature|land|artifact|enchantment|planeswalker|battle|permanent|equipment|aura|vehicle|saga|class|case|room|siege|token|object|kindred|background|contraption|attraction|spacecraft|planet|role)\M";
}
