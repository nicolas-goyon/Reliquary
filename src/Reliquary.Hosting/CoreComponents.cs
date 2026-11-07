using Reliquary.Accounts;
using Reliquary.BuildingBlocks;
using Reliquary.Cards;
using Reliquary.Collection;
using Reliquary.Decks;
using Reliquary.Playtest;

namespace Reliquary.Hosting;

/// <summary>
/// The list of the core components that the hosts register.
/// </summary>
public static class CoreComponents
{
    /// <summary>
    /// Gets all core components, in the order of registration.
    /// </summary>
    public static IReadOnlyList<ICoreComponent> All { get; } =
    [
        new AccountsComponent(),
        new CardsComponent(),
        new DecksComponent(),
        new CollectionComponent(),
        new PlaytestComponent(),
    ];
}
