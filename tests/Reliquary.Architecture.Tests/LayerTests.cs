using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Reliquary.Accounts;
using Reliquary.BuildingBlocks;
using Reliquary.Cards;
using Reliquary.Collection;
using Reliquary.Decks;
using Reliquary.Playtest;

namespace Reliquary.Architecture.Tests;

/// <summary>
/// Checks the rules between the layers: building blocks, platform, hosting, hosts and core components.
/// See docs/decisions/0007-modular-monolith.md and docs/decisions/0019-hosts-and-scheduled-jobs.md.
/// </summary>
public sealed class LayerTests
{
    private static readonly string[] Hosts = ["Reliquary.Api", "Reliquary.Worker", "Reliquary.Cli"];

    private static readonly Assembly[] ComponentAssemblies =
    [
        typeof(AccountsComponent).Assembly,
        typeof(CardsComponent).Assembly,
        typeof(CollectionComponent).Assembly,
        typeof(DecksComponent).Assembly,
        typeof(PlaytestComponent).Assembly,
    ];

    [Fact]
    public void BuildingBlocksProjectReferencesNoOtherProject()
    {
        var references = ProjectReferences(Path.Combine(Source, "Reliquary.BuildingBlocks", "Reliquary.BuildingBlocks.csproj"));

        Assert.Empty(references);
    }

    [Fact]
    public void BuildingBlocksAssemblyUsesNoOtherReliquaryAssembly()
    {
        var assembly = typeof(ICoreComponent).Assembly;

        var violations = assembly.GetReferencedAssemblies()
            .Where(reference => reference.Name!.StartsWith("Reliquary.", StringComparison.Ordinal))
            .Select(reference => $"{assembly.GetName().Name} uses {reference.Name}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void PlatformProjectReferencesOnlyTheBuildingBlocks()
    {
        var references = ProjectReferences(Path.Combine(Source, "Reliquary.Platform", "Reliquary.Platform.csproj"));

        Assert.Equal(["Reliquary.BuildingBlocks.csproj"], references.Select(Path.GetFileName));
    }

    [Theory]
    [MemberData(nameof(HostNames))]
    public void HostProjectReferencesOnlyTheHosting(string host)
    {
        var references = ProjectReferences(Path.Combine(Source, host, $"{host}.csproj"));

        Assert.Equal(["Reliquary.Hosting.csproj"], references.Select(Path.GetFileName));
    }

    [Fact]
    public void CoreComponentProjectsShowTheirInternalsOnlyToTheirOwnTests()
    {
        var projectFiles = Directory.GetFiles(Path.Combine(Source, "Components"), "*.csproj", SearchOption.AllDirectories);

        var violations = projectFiles
            .SelectMany(projectFile => XDocument.Load(projectFile)
                .Descendants("InternalsVisibleTo")
                .Select(item => item.Attribute("Include")!.Value)
                .Where(friend => friend != $"{Path.GetFileNameWithoutExtension(projectFile)}.Tests")
                .Select(friend => $"{Path.GetFileName(projectFile)} shows its internal types to {friend}"))
            .ToList();

        Assert.NotEmpty(projectFiles);
        Assert.Empty(violations);
    }

    [Fact]
    public void CoreComponentAssembliesShowTheirInternalsOnlyToTheirOwnTests()
    {
        var violations = ComponentAssemblies
            .SelectMany(assembly => assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim())
                .Where(friend => friend != $"{assembly.GetName().Name}.Tests")
                .Select(friend => $"{assembly.GetName().Name} shows its internal types to {friend}"))
            .ToList();

        Assert.Empty(violations);
    }

    public static TheoryData<string> HostNames() => new(Hosts);

    private static string Source => Path.Combine(RepositoryRoot.Path, "src");

    /// <summary>
    /// Gives the full paths of the projects that a project file references.
    /// </summary>
    private static List<string> ProjectReferences(string projectFile)
    {
        Assert.True(File.Exists(projectFile), $"The project file {projectFile} does not exist.");
        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(projectFile)!,
                reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();
    }
}
