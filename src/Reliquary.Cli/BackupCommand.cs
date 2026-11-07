using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Platform.Backup;

namespace Reliquary.Cli;

/// <summary>
/// The <c>backup</c> and <c>restore</c> commands: they save all the databases of the instance to a folder, and restore them.
/// </summary>
internal static class BackupCommand
{
    public static Command CreateBackup()
    {
        var directory = new Argument<string>("directory") { Description = "An empty folder for the backup. The command creates it when it does not exist." };
        var backup = new Command("backup", "Back up all the databases of the instance: the users database, the reference database and each shard.") { directory };
        backup.SetAction(async (result, cancellationToken) =>
        {
            using var host = CliHost.Build();
            try
            {
                var manifest = await host.Services.GetRequiredService<InstanceBackup>().BackupAsync(result.GetValue(directory)!, cancellationToken);
                Console.WriteLine($"The backup contains {manifest.Databases.Count} databases.");
                return 0;
            }
            catch (InvalidOperationException exception)
            {
                await Console.Error.WriteLineAsync(exception.Message);
                return 1;
            }
        });
        return backup;
    }

    public static Command CreateRestore()
    {
        var directory = new Argument<string>("directory") { Description = "The folder of a backup." };
        var replace = new Option<bool>("--replace") { Description = "Replace the data of an instance that has data. Without this option, the command restores only into an empty instance." };
        var restore = new Command("restore", "Restore a backup into the databases of the instance. Stop the API and the worker first.") { directory, replace };
        restore.SetAction(async (result, cancellationToken) =>
        {
            using var host = CliHost.Build();
            try
            {
                var manifest = await host.Services.GetRequiredService<InstanceBackup>().RestoreAsync(result.GetValue(directory)!, result.GetValue(replace), cancellationToken);
                Console.WriteLine($"The command restored {manifest.Databases.Count} databases from the backup of {manifest.CreatedAt:u}. Run \"reliquary migrate\" before you start the instance.");
                return 0;
            }
            catch (InvalidOperationException exception)
            {
                await Console.Error.WriteLineAsync(exception.Message);
                return 1;
            }
        });
        return restore;
    }
}
