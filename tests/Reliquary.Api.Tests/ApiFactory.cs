using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Reliquary.Api.Tests;

/// <summary>
/// Starts the API host for the tests, in the environment "Testing".
/// The host does not read appsettings.Development.json, so it does not connect to a local database by default.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Gets the settings that replace the configuration of the host.</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>Gets or sets endpoints that only the tests use. They run before the endpoints of the API.</summary>
    public Action<IEndpointRouteBuilder>? TestEndpoints { get; set; }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        if (TestEndpoints is { } endpoints)
        {
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new TestEndpointsFilter(endpoints)));
        }
    }

    private sealed class TestEndpointsFilter(Action<IEndpointRouteBuilder> endpoints) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints);
            next(app);
        };
    }
}
