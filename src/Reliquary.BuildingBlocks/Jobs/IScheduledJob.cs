namespace Reliquary.BuildingBlocks.Jobs;

/// <summary>
/// A task that the worker process runs again after the same interval each time, for example the daily import of the card data.
/// </summary>
/// <remarks>
/// Only one worker replica runs a job at a time. A lock in the database of the <c>users</c> schema stops the other replicas.
/// The worker records the start and the end of each run, so a restart does not run a job too early.
/// </remarks>
public interface IScheduledJob
{
    /// <summary>
    /// Gets the unique name of the job, for example <c>cards.import</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the minimum time between the end of a run and the start of the next run.
    /// </summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Runs the job one time.
    /// </summary>
    /// <param name="run">The options of this run.</param>
    /// <param name="cancellationToken">A token that stops the job.</param>
    /// <returns>A task that completes at the end of the run.</returns>
    Task RunAsync(JobRun run, CancellationToken cancellationToken);
}

/// <summary>
/// The options of one run of a scheduled job.
/// </summary>
/// <param name="Force">
/// <see langword="true"/> when an administrator starts the run, for example with <c>reliquary jobs run</c>.
/// The job then does all its work, also when its sources did not change.
/// </param>
public sealed record JobRun(bool Force);
