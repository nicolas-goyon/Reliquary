using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.Platform.Data;

namespace Reliquary.Platform.Migrations;

/// <summary>
/// Applies the migrations when the host starts, if <c>Database:MigrateOnStartup</c> is <see langword="true"/>.
/// </summary>
/// <remarks>
/// The host starts its other services after this service completes.
/// In production, run the <c>migrate</c> command of the CLI before a deployment instead.
/// </remarks>
/// <param name="runner">The migration runner.</param>
/// <param name="options">The database settings.</param>
/// <param name="logger">The logger.</param>
internal sealed class MigrateOnStartupService(MigrationRunner runner, IOptions<DatabaseOptions> options, ILogger<MigrateOnStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        var applied = await runner.MigrateAsync(cancellationToken);
        logger.MigratedOnStartup(applied);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
