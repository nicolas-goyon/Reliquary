using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Platform.Jobs;

namespace Reliquary.Cli;

/// <summary>
/// The <c>jobs</c> commands: they list the scheduled jobs and run a job now.
/// </summary>
internal static class JobsCommand
{
    public static Command Create()
    {
        var list = new Command("list", "List the scheduled jobs and their intervals.");
        list.SetAction(_ =>
        {
            using var host = CliHost.Build();
            foreach (var job in host.Services.GetServices<IScheduledJob>().OrderBy(job => job.Name, StringComparer.Ordinal))
            {
                Console.WriteLine($"{job.Name} (every {job.Interval})");
            }

            return 0;
        });

        var name = new Argument<string>("name") { Description = "The name of the job, for example cards.import." };
        var force = new Option<bool>("--force")
        {
            Description = "Do all the work, also when the sources did not change. Without this option, the job can skip unchanged sources.",
        };
        var run = new Command("run", "Run a scheduled job now, without a wait for its interval.") { name, force };
        run.SetAction(async (result, cancellationToken) =>
        {
            using var host = CliHost.Build();
            var jobName = result.GetValue(name);
            var job = host.Services.GetServices<IScheduledJob>().FirstOrDefault(job => job.Name == jobName);
            if (job is null)
            {
                await Console.Error.WriteLineAsync($"The job \"{jobName}\" does not exist. Run \"reliquary jobs list\" to see the jobs.");
                return 2;
            }

            var runner = host.Services.GetRequiredService<ScheduledJobRunner>();
            var outcome = await runner.RunIfDueAsync(job, result.GetValue(force), cancellationToken);
            Console.WriteLine($"The job {job.Name} ended with the result: {outcome}.");
            return outcome is JobRunResult.Succeeded or JobRunResult.NotDue ? 0 : 1;
        });

        return new Command("jobs", "List and run the scheduled jobs.") { list, run };
    }
}
