using Reliquary.BuildingBlocks.Collection;
using Reliquary.Decks.Purchases;

namespace Reliquary.Decks.Tests.Purchases;

/// <summary>
/// The gate of milestone M2: the purchase list of a reference dataset, card by card. See docs/architecture/purchase-list.md.
/// </summary>
public sealed class PurchaseListGateTests
{
    private static readonly Guid DeckId = Guid.Parse("00000000-0000-0000-0000-00000000d001");
    private static readonly Guid MainDeckId = Guid.Parse("00000000-0000-0000-0000-00000000d002");
    private static readonly Guid OtherDeckId = Guid.Parse("00000000-0000-0000-0000-00000000d003");
    private static readonly Guid WantedPrinting = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherPrinting = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid AvoidedPrinting = Guid.Parse("00000000-0000-0000-0000-0000000000a3");

    /// <summary>The locations of the reference collection.</summary>
    private static readonly Dictionary<string, (Guid Id, string Kind, bool Locked, bool Pickable, Guid? DeckId)> Locations = new()
    {
        ["Rack"] = (Guid.Parse("00000000-0000-0000-0000-0000000000b1"), LocationKinds.Rack, false, true, null),
        ["Binder"] = (Guid.Parse("00000000-0000-0000-0000-0000000000b2"), LocationKinds.Binder, true, true, null),
        ["Showcase"] = (Guid.Parse("00000000-0000-0000-0000-0000000000b3"), LocationKinds.Box, false, false, null),
        ["This deck"] = (Guid.Parse("00000000-0000-0000-0000-0000000000b4"), LocationKinds.Deck, false, true, DeckId),
        ["Main deck"] = (Guid.Parse("00000000-0000-0000-0000-0000000000b5"), LocationKinds.Deck, true, true, MainDeckId),
        ["Other deck"] = (Guid.Parse("00000000-0000-0000-0000-0000000000b6"), LocationKinds.Deck, false, true, OtherDeckId),
    };

    /// <summary>
    /// The reference dataset: for each card, the lines of the deck, the copies of the collection and the expected result of each line.
    /// </summary>
    public static TheoryData<string> Cards => [.. Reference.Keys];

    private static readonly Dictionary<string, Case> Reference = new()
    {
        ["A card in the location of the deck is already in the deck"] = new(
            [new Need("main", 1)],
            [new Copy("This deck", 1)],
            [new Expected(1, [], 0, [])]),
        ["The cards of another assembled deck are never available"] = new(
            [new Need("main", 10)],
            [new Copy("This deck", 4), new Copy("Rack", 3), new Copy("Other deck", 5)],
            [new Expected(4, [("Rack", 3)], 3, [("Other deck", 5, "unavailable")])]),
        ["A copy in another language is the closest copy"] = new(
            [new Need("main", 1, new PurchasePreferences(Languages: ["fr"]))],
            [new Copy("Rack", 1)],
            [new Expected(0, [], 1, [("Rack", 1, "language")])]),
        ["The unlocked locations come before the locked locations"] = new(
            [new Need("main", 2, new PurchasePreferences(Foil: "nonfoil"))],
            [new Copy("Binder", 2), new Copy("Rack", 1, Finish: "foil"), new Copy("Rack", 1, Finish: "nonfoil")],
            [new Expected(0, [("Rack", 1), ("Binder", 1)], 0, [])]),
        ["The cards of a location that is not pickable are not available"] = new(
            [new Need("main", 1)],
            [new Copy("Showcase", 1)],
            [new Expected(0, [], 1, [("Showcase", 1, "unavailable")])]),
        ["A wanted printing refuses the other printings and the copies with no printing"] = new(
            [new Need("main", 1, new PurchasePreferences(WantedPrintingIds: [WantedPrinting]))],
            [new Copy("Rack", 1, Printing: OtherPrinting), new Copy("Rack", 1)],
            [new Expected(0, [], 1, [("Rack", 1, "printing"), ("Rack", 1, "printing")])]),
        ["An avoided printing is refused"] = new(
            [new Need("main", 1, new PurchasePreferences(AvoidedPrintingIds: [AvoidedPrinting]))],
            [new Copy("Rack", 1, Printing: AvoidedPrinting), new Copy("Rack", 1, Printing: WantedPrinting)],
            [new Expected(0, [("Rack", 1)], 0, [])]),
        ["The line with preferences takes its copies first"] = new(
            [new Need("sideboard", 1), new Need("main", 1, new PurchasePreferences(Languages: ["fr"]))],
            [new Copy("Rack", 1, Language: "fr")],
            [new Expected(0, [], 1, []), new Expected(0, [("Rack", 1)], 0, [])]),
        ["A copy with no condition agrees with a minimum condition"] = new(
            [new Need("main", 2, new PurchasePreferences(MinimumCondition: "near_mint"))],
            [new Copy("Rack", 1, Condition: "played"), new Copy("Rack", 1), new Copy("Binder", 1, Condition: "mint")],
            [new Expected(0, [("Rack", 1), ("Binder", 1)], 0, [])]),
        ["A worse condition is the closest copy"] = new(
            [new Need("main", 1, new PurchasePreferences(MinimumCondition: "excellent"))],
            [new Copy("Rack", 1, Condition: "played")],
            [new Expected(0, [], 1, [("Rack", 1, "condition")])]),
        ["A variant takes the cards of its main deck first, also from a locked location"] = new(
            [new Need("main", 2)],
            [new Copy("Main deck", 1), new Copy("Rack", 2)],
            [new Expected(1, [("Rack", 1)], 0, [])]),
        ["A card that is not in the collection is a card to buy"] = new(
            [new Need("main", 3)],
            [],
            [new Expected(0, [], 3, [])]),
        ["The closest copies with fewer reasons come first"] = new(
            [new Need("main", 1, new PurchasePreferences(Languages: ["fr"], Foil: "foil"))],
            [new Copy("Rack", 1, Language: "en", Finish: "nonfoil"), new Copy("Other deck", 1, Language: "fr", Finish: "foil"), new Copy("Rack", 1, Language: "fr", Finish: "nonfoil")],
            [new Expected(0, [], 1, [("Rack", 1, "finish"), ("Other deck", 1, "unavailable"), ("Rack", 1, "language,finish")])]),
        ["A foil preference accepts the etched finish"] = new(
            [new Need("main", 1, new PurchasePreferences(Foil: "foil"))],
            [new Copy("Rack", 1, Finish: "etched")],
            [new Expected(0, [("Rack", 1)], 0, [])]),
    };

    [Theory]
    [MemberData(nameof(Cards))]
    public void PurchaseListOfTheReferenceDatasetIsCorrect(string card)
    {
        var reference = Reference[card];
        var oracleId = Guid.NewGuid();
        var needs = reference.Needs.Select(need => new PurchaseNeed(oracleId, need.Zone, need.Quantity, need.Preferences ?? PurchasePreferences.None)).ToList();
        var copies = reference.Copies.Select(copy => ToCopy(copy, oracleId)).ToList();

        var plan = PurchasePlanner.Plan(needs, copies, new HashSet<Guid> { DeckId, MainDeckId });

        Assert.Equal(reference.Expected.Count, plan.Count);
        foreach (var (expected, line) in reference.Expected.Zip(plan))
        {
            Assert.Equal(expected.InDeck, line.FromDeck.Sum(taken => taken.Quantity));
            Assert.Equal(expected.FromCollection, [.. line.FromCollection.Select(taken => (taken.Copy.LocationName, taken.Quantity))]);
            Assert.Equal(expected.ToBuy, line.ToBuy);
            Assert.Equal(expected.Closest, [.. line.Closest.Select(other => (other.Copy.LocationName, other.Quantity, string.Join(',', other.Reasons)))]);
        }
    }

    [Fact]
    public void ReferenceDatasetCoversEachReason()
    {
        var reasons = Reference.Values.SelectMany(reference => reference.Expected).SelectMany(expected => expected.Closest).SelectMany(closest => closest.Reasons.Split(',')).ToHashSet();

        Assert.Equal(["condition", "finish", "language", "printing", "unavailable"], reasons.Order());
    }

    private static CollectionCopy ToCopy(Copy copy, Guid oracleId)
    {
        var location = Locations[copy.Location];
        return new CollectionCopy(location.Id, copy.Location, null, location.Kind, location.Locked, location.Pickable, location.DeckId, oracleId, copy.Printing, copy.Language, copy.Finish, copy.Condition, copy.Quantity);
    }

    private sealed record Case(IReadOnlyList<Need> Needs, IReadOnlyList<Copy> Copies, IReadOnlyList<Expected> Expected);

    private sealed record Need(string Zone, int Quantity, PurchasePreferences? Preferences = null);

    private sealed record Copy(string Location, int Quantity, Guid? Printing = null, string Language = "en", string? Finish = null, string? Condition = null);

    private sealed record Expected(int InDeck, IReadOnlyList<(string Location, int Quantity)> FromCollection, int ToBuy, IReadOnlyList<(string Location, int Quantity, string Reasons)> Closest);
}
