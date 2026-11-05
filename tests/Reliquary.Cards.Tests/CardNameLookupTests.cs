using Reliquary.Cards.Catalog;
using Reliquary.Cards.Text;

namespace Reliquary.Cards.Tests;

public sealed class CardNameLookupTests
{
    [Theory]
    [InlineData("Sundering Eruption / Volcanic Fissure")]
    [InlineData("Sundering Eruption // Volcanic Fissure")]
    [InlineData("Sundering Eruption /// Volcanic Fissure")]
    public void EachSlashFormGivesTheSameName(string name) =>
        Assert.Equal(NameNormalizer.Normalize("Sundering Eruption // Volcanic Fissure"), CardCatalog.NormalizeForLookup(name));
}
