using Reliquary.Cards.Text;

namespace Reliquary.Cards.Tests;

public sealed class TextTests
{
    [Theory]
    [InlineData("Lightning Bolt", "lightning bolt")]
    [InlineData("Éclair", "eclair")]
    [InlineData("Æther Vial", "aether vial")]
    [InlineData("Jötun Grunt", "jotun grunt")]
    [InlineData("Atraxa, Praetors’ Voice", "atraxa, praetors' voice")]
    [InlineData("  Sol   Ring ", "sol ring")]
    [InlineData("Fire // Ice", "fire // ice")]
    [InlineData("Straße", "strasse")]
    [InlineData("Œuvre", "oeuvre")]
    [InlineData("Ластівка", "ластівка")]
    public void NormalizeGivesTheComparisonForm(string name, string expected) =>
        Assert.Equal(expected, NameNormalizer.Normalize(name));

    [Fact]
    public void NormalizeKeepsTheMarksOfJapaneseLetters()
    {
        // "ガ" is "カ" with a voiced mark. The mark changes the letter, so it must stay.
        Assert.Equal("ガ", NameNormalizer.Normalize("ガ"));
        Assert.NotEqual(NameNormalizer.Normalize("カ"), NameNormalizer.Normalize("ガ"));
    }

    [Theory]
    [InlineData("{2}{W}{U}", 4)]
    [InlineData("{X}{R}", 1)]
    [InlineData("{2/W}{2/U}", 4)]
    [InlineData("{W/P}", 1)]
    [InlineData("{W/U}{W/U}", 2)]
    [InlineData("{½}", 0.5)]
    [InlineData("{HW}", 0.5)]
    [InlineData("{1000000}", 1000000)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ValueFollowsTheRulesOfTheGame(string? cost, double expected) =>
        Assert.Equal((decimal)expected, ManaCost.Value(cost));

    [Theory]
    [InlineData("{1}{R}", "R")]
    [InlineData("{W/U}{B}", "W,U,B")]
    [InlineData("{2}{G/P}", "G")]
    [InlineData("{5}", "")]
    public void ColorsComeFromTheSymbols(string cost, string expected) =>
        Assert.Equal(expected, string.Join(',', ManaCost.Colors(cost)));

    [Fact]
    public void MaskGivesOneBitForEachColor()
    {
        Assert.Equal(0, ManaCost.Mask([]));
        Assert.Equal(1 | 2, ManaCost.Mask(["W", "U"]));
        Assert.Equal(16 | 32, ManaCost.Mask(["G", "C"]));
    }
}
