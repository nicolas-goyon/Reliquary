using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform.Migrations;

namespace Reliquary.Cli;

/// <summary>
/// The <c>migrate</c> command: it applies the database migrations to all databases.
/// </summary>
internal static class MigrateCommand
{
    public static Command Create()
    {
        var check = new Option<bool>("--check")
        {
            Description = "List the pending scripts, but do not apply them. The exit code is 1 when a script is pending.",
        };

        var command = new Command("migrate", "Apply the database migrations to the users database, the reference database and each shard database.")
        {
            check,
        };

        command.SetAction(async (result, cancellationToken) =>
        {
            using var host = CliHost.Build();
            var runner = host.Services.GetRequiredService<MigrationRunner>();

            if (result.GetValue(check))
            {
                var pending = await runner.PendingAsync(cancellationToken);
                foreach (var (database, script) in pending)
                {
                    Console.WriteLine($"{database}: {script}");
                }

                Console.WriteLine($"{pending.Count} scripts are pending.");
                return pending.Count == 0 ? 0 : 1;
            }

            var applied = await runner.MigrateAsync(cancellationToken);
            Console.WriteLine($"The databases are up to date. The command applied {applied} scripts.");
            return 0;
        });

        return command;
    }
}
