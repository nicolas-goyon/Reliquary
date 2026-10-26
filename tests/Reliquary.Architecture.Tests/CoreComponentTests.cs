using System.Reflection;
using System.Xml.Linq;
using Reliquary.Accounts;
using Reliquary.BuildingBlocks;
using Reliquary.Cards;
using Reliquary.Collection;
using Reliquary.Decks;
using Reliquary.Playtest;

namespace Reliquary.Architecture.Tests;

/// <summary>
/// Checks the rules of the modular monolith. See docs/decisions/0007-modular-monolith.md.
/// </summary>
public sealed class CoreComponentTests
{
    private static readonly Assembly[] ComponentAssemblies =
    [
        typeof(AccountsComponent).Assembly,
        typeof(CardsComponent).Assembly,
        typeof(CollectionComponent).Assembly,
        typeof(DecksComponent).Assembly,
        typeof(PlaytestComponent).Assembly,
    ];

    [Fact]
    public void CoreComponentsDoNotUseEachOther()
    {
        var componentNames = ComponentAssemblies.Select(assembly => assembly.GetName().Name!).ToHashSet();

        var violations = ComponentAssemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies()
                .Where(reference => componentNames.Contains(reference.Name!))
                .Select(reference => $"{assembly.GetName().Name} uses {reference.Name}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void CoreComponentProjectsDoNotReferenceEachOther()
    {
        var componentsFolder = Path.Combine(RepositoryRoot.Path, "src", "Components");
        var projectFiles = Directory.GetFiles(componentsFolder, "*.csproj", SearchOption.AllDirectories);

        var violations = projectFiles
            .SelectMany(projectFile => XDocument.Load(projectFile)
                .Descendants("ProjectReference")
                .Select(reference => Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(projectFile)!,
                    reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))))
                .Where(target => target.StartsWith(componentsFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Select(target => $"{Path.GetFileName(projectFile)} references {Path.GetFileName(target)}"))
            .ToList();

        Assert.NotEmpty(projectFiles);
        Assert.Empty(violations);
    }

    [Fact]
    public void EachCoreComponentHasOneRegistrationClass()
    {
        foreach (var assembly in ComponentAssemblies)
        {
            var registrations = assembly.GetExportedTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ICoreComponent).IsAssignableFrom(type))
                .ToList();

            Assert.Single(registrations);
        }
    }
}
