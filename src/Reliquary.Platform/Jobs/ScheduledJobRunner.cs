using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Platform.Data;

namespace Reliquary.Platform.Jobs;

/// <summary>
/// The settings of the scheduled jobs. The section is <c>Jobs</c>.
/// </summary>
public sealed class JobOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Jobs";

    /// <summary>Gets or sets the time between two checks of the job schedule.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the names of the jobs that the worker does not run.</summary>
    public List<string> Disabled { get; set; } = [];
}

/// <summary>
/// Runs the scheduled jobs of the worker process.
/// </summary>
/// <remarks>
/// For each job, the runner checks the table <c>users.job_runs</c>. When the interval since the end of the last run
/// is complete, the runner takes an advisory lock and runs the job. Only one replica can hold the lock.
/// </remarks>
/// <param name="jobs">The scheduled jobs.</param>
/// <param name="sources">The connection settings.</param>
/// <param name="options">The settings of the jobs.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class ScheduledJobRunner(
    IEnumerable<IScheduledJob> jobs,
    DataSources sources,
    IOptions<JobOptions> options,
    TimeProvider time,
    ILogger<ScheduledJobRunner> logger) : BackgroundService
{
    /// <summary>
    /// Runs a job when it is due and no other replica runs it.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="force">When <see langword="true"/>, the runner ignores the interval, and the job does all its work.</param>
    /// <param name="cancellationToken">A token that stops the job.</param>
    /// <returns>The result of the attempt.</returns>
    public async Task<JobRunResult> RunIfDueAsync(IScheduledJob job, bool force, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var connection = await sources.Pool(sources.Users).OpenConnectionAsync(cancellationToken);
        var key = StableHash.Of($"reliquary.job.{job.Name}");
        var locked = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select pg_try_advisory_lock(@key)", new { key }, cancellationToken: cancellationToken));
        if (!locked)
        {
            return JobRunResult.Busy;
        }

        try
        {
            if (!force && !await IsDueAsync(connection, job, cancellationToken))
            {
                return JobRunResult.NotDue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into users.job_runs (name, last_started_at, last_status)
                values (@name, @now, 'running')
                on conflict (name) do update set last_started_at = excluded.last_started_at, last_status = 'running'
                """,
                new { name = job.Name, now = time.GetUtcNow() },
                cancellationToken: cancellationToken));

            logger.JobStarted(job.Name);
            try
            {
                await job.RunAsync(new JobRun(force), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.JobFailed(exception, job.Name);
                await RecordEndAsync(connection, job, "failed", exception.Message);
                return JobRunResult.Failed;
            }

            await RecordEndAsync(connection, job, "succeeded", null);
            logger.JobCompleted(job.Name);
            return JobRunResult.Succeeded;
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "select pg_advisory_unlock(@key)", new { key }, cancellationToken: CancellationToken.None));
        }
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var active = jobs.Where(job => !options.Value.Disabled.Contains(job.Name, StringComparer.Ordinal)).ToList();
        return Task.WhenAll(active.Select(job => LoopAsync(job, stoppingToken)));
    }

    private async Task LoopAsync(IScheduledJob job, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIfDueAsync(job, force: false, stoppingToken);
                await Task.Delay(options.Value.CheckInterval, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The loop must continue after an error, for example when the database is not available.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.JobCheckFailed(exception, job.Name, options.Value.CheckInterval);
                await Task.Delay(options.Value.CheckInterval, time, stoppingToken);
            }
        }
    }

    private async Task<bool> IsDueAsync(NpgsqlConnection connection, IScheduledJob job, CancellationToken cancellationToken)
    {
        var lastFinished = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "select last_finished_at from users.job_runs where name = @name",
            new { name = job.Name },
            cancellationToken: cancellationToken));

        return lastFinished is null || time.GetUtcNow() - new DateTimeOffset(lastFinished.Value, TimeSpan.Zero) >= job.Interval;
    }

    private async Task RecordEndAsync(NpgsqlConnection connection, IScheduledJob job, string status, string? error) =>
        await connection.ExecuteAsync(new CommandDefinition(
            "update users.job_runs set last_finished_at = @now, last_status = @status, last_error = @error where name = @name",
            new { name = job.Name, now = time.GetUtcNow(), status, error },
            cancellationToken: CancellationToken.None));
}

/// <summary>
/// The result of an attempt to run a scheduled job.
/// </summary>
public enum JobRunResult
{
    /// <summary>The job ran and completed.</summary>
    Succeeded,

    /// <summary>The job ran and failed.</summary>
    Failed,

    /// <summary>The interval since the last run is not complete.</summary>
    NotDue,

    /// <summary>Another replica runs the job.</summary>
    Busy,
}
