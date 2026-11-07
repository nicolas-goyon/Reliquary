using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Hosting;

namespace Reliquary.Api.Tests;

/// <summary>
/// Checks that the worker and the CLI can build their services. They use a generic host, not the web host of the API.
/// </summary>
public sealed class HostServicesTests
{
    [Fact]
    public void WorkerAndCliServicesAreComplete()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Users"] = "Host=localhost;Database=unused",
            ["ConnectionStrings:Redis"] = "localhost:1",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddReliquaryCore(configuration);
        services.AddReliquaryWorker(configuration);

        // The generic host validates the services in the Development environment in the same way.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider);
    }
}
