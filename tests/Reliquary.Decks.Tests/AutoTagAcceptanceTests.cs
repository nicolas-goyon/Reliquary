using Reliquary.Decks.AutoTag;

namespace Reliquary.Decks.Tests;

/// <summary>
/// The acceptance test of issue #97: auto-tag gives the correct categories to at least 80 % of the cards of 10 reference decks.
/// </summary>
public sealed class AutoTagAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public void AutoTagIsCorrectForAtLeast80PercentOfTheReferenceCards()
    {
        var decks = AutoTagFixtures.ReferenceDecks();
        Assert.Equal(10, decks.Count);

        var (correct, total) = Measure(decks, withTags: true);
        var accuracy = (double)correct / total;
        output.WriteLine($"Total: {correct} of {total} cards are correct ({accuracy:P1}).");
        Assert.True(accuracy >= 0.80, $"Auto-tag is correct for {accuracy:P1} of the reference cards. The minimum is 80 %.");
    }

    [Fact]
    public void TextRulesAloneGiveAResultForCardsWithNoTag()
    {
        // This test gives the accuracy of the text rules alone, as for a new card that Tagger does not know.
        // The acceptance threshold applies to the full auto-tag only, so this test only records the value.
        var (correct, total) = Measure(AutoTagFixtures.ReferenceDecks(), withTags: false);
        output.WriteLine($"Text rules only: {correct} of {total} cards are correct ({(double)correct / total:P1}).");
        Assert.True(correct > total / 2, $"The text rules alone are correct for {correct} of {total} cards.");
    }

    private (int Correct, int Total) Measure(IReadOnlyList<ReferenceDeck> decks, bool withTags)
    {
        var correct = 0;
        var total = 0;
        foreach (var deck in decks)
        {
            var deckCorrect = 0;
            foreach (var card in deck.Cards)
            {
                var facts = AutoTagFixtures.Card(card.Name);
                var suggestion = AutoTagger.Suggest(facts, withTags ? AutoTagFixtures.TagsOf(facts) : []);
                if (card.IsCorrect(suggestion.Categories))
                {
                    deckCorrect++;
                }
                else
                {
                    output.WriteLine($"  {deck.Name} / {card.Name}: expected [{string.Join(", ", card.Categories)}] (accepted [{string.Join(", ", card.Accepted)}]), suggested [{string.Join(", ", suggestion.Categories)}] from {string.Join(", ", suggestion.Reasons.Select(reason => reason.Detail))}");
                }
            }

            output.WriteLine($"{deck.Name}: {deckCorrect} of {deck.Cards.Count}");
            correct += deckCorrect;
            total += deck.Cards.Count;
        }

        return (correct, total);
    }
}
