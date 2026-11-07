using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reliquary.BuildingBlocks.Demo;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Platform.Jobs;

namespace Reliquary.Cli;

/// <summary>
/// The <c>demo</c> command: it loads the demo dataset into an instance.
/// </summary>
/// <remarks>
/// The command runs the scheduled jobs of the setting <c>Demo:Jobs</c> (default: <c>cards.import</c>),
/// then the demo seeders of the core components. Set <c>Cards:Import:Directory</c> to the folder of the demo cards,
/// for example <c>data/demo</c>, so the import downloads nothing.
/// </remarks>
internal static class DemoCommand
{
    public static Command Create()
    {
        var load = new Command("load", "Load the demo dataset: the demo cards and the demo accounts. The passwords of the demo accounts are public.");
        load.SetAction(async (_, cancellationToken) =>
        {
            using var host = CliHost.Build();
            return await LoadAsync(host, cancellationToken);
        });

        return new Command("demo", "Load the demo dataset into a local instance.") { load };
    }

    private static async Task<int> LoadAsync(IHost host, CancellationToken cancellationToken)
    {
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var jobNames = configuration.GetSection("Demo:Jobs").Get<string[]>() ?? ["cards.import"];
        var jobs = host.Services.GetServices<IScheduledJob>().ToDictionary(job => job.Name, StringComparer.Ordinal);
        var runner = host.Services.GetRequiredService<ScheduledJobRunner>();
        foreach (var name in jobNames)
        {
            if (!jobs.TryGetValue(name, out var job))
            {
                await Console.Error.WriteLineAsync($"The job \"{name}\" does not exist. Correct the setting Demo:Jobs.");
                return 2;
            }

            var outcome = await runner.RunIfDueAsync(job, force: false, cancellationToken);
            for (var attempt = 0; outcome == JobRunResult.Busy && attempt < 300; attempt++)
            {
                // The worker runs the job now. Wait until it completes.
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                outcome = await runner.RunIfDueAsync(job, force: false, cancellationToken);
            }

            Console.WriteLine($"The job {name} ended with the result: {outcome}.");
            if (outcome is not (JobRunResult.Succeeded or JobRunResult.NotDue))
            {
                return 1;
            }
        }

        foreach (var seeder in host.Services.GetServices<IDemoDataSeeder>().OrderBy(seeder => seeder.Order))
        {
            Console.WriteLine($"{seeder.Name}: {await seeder.SeedAsync(cancellationToken)}");
        }

        return 0;
    }
}
