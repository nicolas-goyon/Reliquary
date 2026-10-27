using Reliquary.BuildingBlocks.Cards;
using Reliquary.Decks.AutoTag;

namespace Reliquary.Decks.Tests;

/// <summary>
/// Checks the map of the Oracle tags, the text rules and the order of priority of auto-tag (ADR 0031).
/// </summary>
public sealed class AutoTagRulesTests
{
    private static CardFacts Card(string typeLine, string text) =>
        AnalysisCards.Normal("Test card", "{2}", 2, typeLine, text);

    private static CardOracleTag Tag(string slug, params string[] ancestors) => new(slug, "median", ancestors);

    private static AutoTagSuggestion FromText(string typeLine, string text) => AutoTagger.Suggest(Card(typeLine, text), []);

    [Theory]
    [InlineData("Artifact", "{T}: Add {C}{C}.", "ramp", "mana_ability")]
    [InlineData("Creature — Elf Druid", "{T}: Add one mana of any color.", "ramp", "mana_ability")]
    [InlineData("Sorcery", "Search your library for a basic land card, put it onto the battlefield tapped, then shuffle.", "ramp", "land_to_battlefield")]
    [InlineData("Sorcery", "Search your library for a Forest card, put that card onto the battlefield, then shuffle.", "ramp", "land_to_battlefield")]
    [InlineData("Enchantment", "You may play an additional land on each of your turns.", "ramp", "extra_land")]
    [InlineData("Creature — Human", "Whenever this creature attacks, create a Treasure token.", "ramp", "treasure")]
    [InlineData("Sorcery", "Draw two cards.", "card_draw", "draw_cards")]
    [InlineData("Enchantment", "At the beginning of your upkeep, you draw a card and you lose 1 life.", "card_draw", "draw_cards")]
    [InlineData("Sorcery", "Exile the top three cards of your library. You may play them this turn.", "card_draw", "impulse_draw")]
    [InlineData("Instant", "Destroy target creature.", "targeted_interaction", "destroy_or_exile_target")]
    [InlineData("Instant", "Exile target nonland permanent.", "targeted_interaction", "destroy_or_exile_target")]
    [InlineData("Instant", "Counter target spell.", "targeted_interaction", "counter_target")]
    [InlineData("Instant", "Counter target enchantment, instant, or sorcery spell.", "targeted_interaction", "counter_target")]
    [InlineData("Instant", "Return target nonland permanent to its owner's hand.", "targeted_interaction", "bounce_target")]
    [InlineData("Instant", "Lightning Bolt deals 3 damage to any target.", "targeted_interaction", "damage_target")]
    [InlineData("Instant", "Target creature gets -3/-3 until end of turn.", "targeted_interaction", "shrink_target")]
    [InlineData("Instant", "The owner of target permanent shuffles it into their library.", "targeted_interaction", "tuck_target")]
    [InlineData("Sorcery", "Target player sacrifices a creature.", "targeted_interaction", "edict")]
    [InlineData("Sorcery", "Destroy all creatures. They can't be regenerated.", "board_wipe", "destroy_all")]
    [InlineData("Sorcery", "All creatures get -X/-X until end of turn.", "board_wipe", "shrink_all")]
    [InlineData("Sorcery", "Each creature gets −2/−2 until end of turn.", "board_wipe", "shrink_all")]
    [InlineData("Sorcery", "This spell deals 13 damage to each creature.", "board_wipe", "damage_all")]
    [InlineData("Sorcery", "Return all nonland permanents to their owners' hands.", "board_wipe", "bounce_all")]
    [InlineData("Instant", "Permanents you control gain hexproof and indestructible until end of turn.", "protection", "keyword_to_yours")]
    [InlineData("Artifact — Equipment", "Equipped creature has shroud and haste.", "protection", "keyword_to_yours")]
    [InlineData("Instant", "Target creature you control gains indestructible until end of turn.", "protection", "keyword_to_yours")]
    [InlineData("Instant", "All permanents you control phase out.", "protection", "phase_out")]
    [InlineData("Creature — Human Cleric", "During your turn, your opponents can't cast spells or activate abilities of artifacts, creatures, or enchantments.", "protection", "silence")]
    [InlineData("Instant", "You may choose new targets for target spell or ability.", "protection", "redirect")]
    [InlineData("Enchantment", "At the beginning of your upkeep, if you control a hundred lands, you win the game.", "win_condition", "win_game")]
    [InlineData("Sorcery", "Each opponent loses X life. You gain life equal to the life lost this way.", "win_condition", "drain_x")]
    [InlineData("Creature — Beast", "When this creature enters, creatures you control gain trample and get +X/+X until end of turn.", "win_condition", "overrun")]
    public void TextRuleGivesTheCategory(string typeLine, string text, string category, string rule)
    {
        var suggestion = FromText(typeLine, text);

        Assert.Contains(category, suggestion.Categories);
        Assert.Contains(suggestion.Reasons, reason => reason.Category == category && reason.Source == AutoTagSources.TextRule && reason.Detail == rule);
        Assert.Equal(AutoTagSources.TextRule, suggestion.Source);
    }

    [Theory]
    [InlineData("Creature — Elf", "Whenever an opponent draws a card, you gain 1 life.")]
    [InlineData("Instant", "Exile target card from a graveyard.")]
    [InlineData("Land", "Exile all cards from target player's graveyard.")]
    [InlineData("Creature — Human", "Hexproof, indestructible")]
    [InlineData("Instant", "Return target creature you control to its owner's hand.")]
    [InlineData("Creature — Goblin", "Put a +1/+1 counter on target creature.")]
    [InlineData("Creature — Bear", "")]
    public void TextRuleIgnoresTheTextOfOtherEffects(string typeLine, string text)
    {
        var suggestion = FromText(typeLine, text);

        Assert.DoesNotContain(suggestion.Categories, category => category != "land");
    }

    [Fact]
    public void ReminderTextGivesNoCategory()
    {
        var suggestion = FromText("Instant", "Overload {6}{U} (You may cast this spell for its overload cost. If you do, change \"target\" in its text to \"each.\")");

        Assert.Empty(suggestion.Categories);
        Assert.Equal(AutoTagSources.None, suggestion.Source);
    }

    [Fact]
    public void LandGetsTheLandCategoryAndNoRamp()
    {
        var land = Card("Land", "{T}: Add {C}{C}. This land deals 2 damage to you.");

        var fromText = AutoTagger.Suggest(land, []);
        var fromTags = AutoTagger.Suggest(land, [Tag("sol-land"), Tag("adds-multiple-mana"), Tag("ramp")]);

        Assert.Equal(["land"], fromText.Categories);
        Assert.Equal(["land"], fromTags.Categories);
        Assert.Equal(new AutoTagReason("land", AutoTagSources.TextRule, AutoTagRules.LandRule), fromText.Reasons.Single());
        Assert.Equal(AutoTagSources.TextRule, fromTags.Source);
    }

    [Fact]
    public void ModalCardReadsItsSpellFaceAndIgnoresItsLandFaceForRamp()
    {
        var card = AnalysisCards.Card("modal_dfc",
        [
            AnalysisCards.Face("Witch Enchanter", "{3}{W}", 4, "Creature — Human Warlock", "When this creature enters, destroy target artifact or enchantment an opponent controls."),
            AnalysisCards.Face("Witch-Blessed Meadow", null, 0, "Land", "As this land enters, you may pay 3 life. If you don't, it enters tapped.\n{T}: Add {W}."),
        ]);

        var suggestion = AutoTagger.Suggest(card, []);

        Assert.Equal(["targeted_interaction"], suggestion.Categories);
    }

    [Fact]
    public void OracleTagGivesTheCategoryOfItsAncestor()
    {
        var suggestion = AutoTagger.Suggest(Card("Artifact", "{T}: Add {C}{C}."), [Tag("mana-rock", "mana-producer", "ramp"), Tag("full-refund", "refund")]);

        Assert.Equal(["ramp"], suggestion.Categories);
        Assert.Equal(new AutoTagReason("ramp", AutoTagSources.OracleTag, "mana-rock"), suggestion.Reasons.Single());
        Assert.Equal(AutoTagSources.OracleTag, suggestion.Source);
    }

    [Fact]
    public void OracleTagsWinOverTheTextRules()
    {
        // The text has "draw a card", but the tags tell that the card is a counterspell only.
        var card = Card("Instant", "Counter target spell. Its controller draws a card.");

        var suggestion = AutoTagger.Suggest(card, [Tag("counterspell", "blue-effect")]);

        Assert.Equal(["targeted_interaction"], suggestion.Categories);
    }

    [Fact]
    public void BoardWipeGetsNoTargetedInteractionFromTheGeneralRemovalTag()
    {
        var wrath = Card("Sorcery", "Destroy all creatures.");
        var rift = Card("Instant", "Return target nonland permanent you don't control to its owner's hand.");

        var wrathSuggestion = AutoTagger.Suggest(wrath, [Tag("sweeper", "removal"), Tag("removal-creature", "removal"), Tag("removal-destroy", "removal")]);
        var riftSuggestion = AutoTagger.Suggest(rift, [Tag("sweeper-one-sided", "sweeper", "removal"), Tag("spot-removal", "removal")]);

        Assert.Equal(["board_wipe"], wrathSuggestion.Categories);
        Assert.Equal(["targeted_interaction", "board_wipe"], riftSuggestion.Categories);
        Assert.Equal("spot-removal", riftSuggestion.Reasons[0].Detail);
    }

    [Fact]
    public void WeakTagGivesNoCategory()
    {
        var suggestion = AutoTagger.Suggest(Card("Creature — Bear", string.Empty), [new CardOracleTag("ramp", "weak", [])]);

        Assert.Empty(suggestion.Categories);
    }

    [Fact]
    public void UserRuleWinsOverTheTagsAndTheText()
    {
        var card = Card("Artifact", "{T}: Add {C}{C}.");

        var suggestion = AutoTagger.Suggest(card, [Tag("mana-rock", "ramp")], ["synergy", "ramp", "unknown"]);
        var empty = AutoTagger.Suggest(card, [Tag("mana-rock", "ramp")], []);

        Assert.Equal(["ramp", "synergy"], suggestion.Categories);
        Assert.All(suggestion.Reasons, reason => Assert.Equal(AutoTagSources.UserRule, reason.Source));
        Assert.Equal(AutoTagSources.UserRule, suggestion.Source);
        Assert.Empty(empty.Categories);
        Assert.Equal(AutoTagSources.UserRule, empty.Source);
    }

    [Fact]
    public void EachOracleTagRuleUsesAKnownCategory()
    {
        Assert.All(AutoTagRules.OracleTags, rule => Assert.Contains(rule.Category, DeckRules.Categories));
        Assert.All(AutoTagRules.TextRules, rule => Assert.Contains(rule.Category, DeckRules.Categories));
        Assert.Equal(AutoTagRules.TextRules.Count, AutoTagRules.TextRules.Select(rule => rule.Code).Distinct().Count());
    }
}
