using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reliquary.Hosting;

namespace Reliquary.Cli;

/// <summary>
/// Creates the services of the CLI from the configuration: the files appsettings.json and
/// appsettings.&lt;environment&gt;.json, then the environment variables.
/// </summary>
internal static class CliHost
{
    /// <summary>
    /// Builds a host with the platform and the core components. The host never starts its background services.
    /// </summary>
    /// <returns>The host. The caller must dispose it.</returns>
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = [],
        });

        builder.Configuration.AddEnvironmentVariables("RELIQUARY_");
        builder.Services.AddReliquaryCore(builder.Configuration);
        builder.Services.AddReliquaryWorker(builder.Configuration);
        return builder.Build();
    }
}
