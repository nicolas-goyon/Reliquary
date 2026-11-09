using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Collection;
using Reliquary.Decks.Purchases;

namespace Reliquary.Decks.Tests.Purchases;

/// <summary>
/// The maximum price of the purchase preferences in the purchase list. See docs/architecture/purchase-list.md.
/// </summary>
public sealed class PurchasePriceTests
{
    private static readonly Guid Cheap = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Expensive = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid FoilOnly = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    private static readonly IReadOnlyList<PrintingPrices> Printings =
    [
        new(Cheap, "en", new Dictionary<string, decimal> { ["usd"] = 1.50m, ["eur"] = 1.20m, ["eur_foil"] = 3.00m }),
        new(Expensive, "en", new Dictionary<string, decimal> { ["usd"] = 30m, ["eur"] = 25m }),
        new(FoilOnly, "fr", new Dictionary<string, decimal> { ["eur_foil"] = 2.00m }),
    ];

    [Theory]
    [InlineData(null, null, "EUR", 1.20)]
    [InlineData(null, null, "USD", 1.50)]
    [InlineData("foil", null, "EUR", 2.00)]
    [InlineData("nonfoil", null, "EUR", 1.20)]
    [InlineData(null, "expensive", "EUR", 25)]
    [InlineData(null, "avoidCheap", "EUR", 2.00)]
    public void LowestPriceUsesThePrintingsAndTheFinishThatFitTheLine(string? foil, string? printings, string currency, double expected)
    {
        var preferences = new PurchasePreferences(
            WantedPrintingIds: printings == "expensive" ? [Expensive] : null,
            AvoidedPrintingIds: printings == "avoidCheap" ? [Cheap] : null,
            Foil: foil,
            MaximumPrice: new PriceLimit(5m, currency));

        var lowest = PurchasePlanner.LowestPrice(preferences, Printings);

        Assert.Equal(new PriceLimit((decimal)expected, currency), lowest);
    }

    [Fact]
    public void LowestPriceIsUnknownForACurrencyWithNoMarketPrice()
    {
        var preferences = new PurchasePreferences(MaximumPrice: new PriceLimit(5m, "GBP"));

        Assert.Null(PurchasePlanner.LowestPrice(preferences, Printings));
    }

    [Fact]
    public void ACardToBuyAboveTheMaximumPriceIsMarked()
    {
        var oracleId = Guid.NewGuid();
        var prices = new Dictionary<Guid, IReadOnlyList<PrintingPrices>> { [oracleId] = [Printings[1]] };
        var needs = new List<PurchaseNeed>
        {
            new(oracleId, "main", 1, new PurchasePreferences(MaximumPrice: new PriceLimit(10m, "EUR"))),
            new(oracleId, "sideboard", 1, new PurchasePreferences(MaximumPrice: new PriceLimit(40m, "EUR"))),
            new(oracleId, "commander", 1, PurchasePreferences.None),
        };

        var plan = PurchasePlanner.Plan(needs, [], new HashSet<Guid>(), prices);

        Assert.Equal((new PriceLimit(25m, "EUR"), true), (plan[0].LowestPrice, plan[0].AboveMaximumPrice));
        Assert.Equal((new PriceLimit(25m, "EUR"), false), (plan[1].LowestPrice, plan[1].AboveMaximumPrice));
        Assert.Equal((null, false), (plan[2].LowestPrice, plan[2].AboveMaximumPrice));
    }

    [Fact]
    public void TheMaximumPriceDoesNotApplyToTheCopiesOfTheCollection()
    {
        var oracleId = Guid.NewGuid();
        var prices = new Dictionary<Guid, IReadOnlyList<PrintingPrices>> { [oracleId] = [Printings[1]] };
        var copy = new CollectionCopy(Guid.NewGuid(), "Rack", null, LocationKinds.Rack, false, true, null, oracleId, Expensive, "en", null, null, 1);
        var need = new PurchaseNeed(oracleId, "main", 1, new PurchasePreferences(MaximumPrice: new PriceLimit(10m, "EUR")));

        var line = PurchasePlanner.Plan([need], [copy], new HashSet<Guid>(), prices).Single();

        Assert.Equal((1, 0, (PriceLimit?)null, false), (line.FromCollection.Sum(taken => taken.Quantity), line.ToBuy, line.LowestPrice, line.AboveMaximumPrice));
    }
}
