using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Platform.Jobs;

namespace Reliquary.Platform.Tests;

public sealed class ScheduledJobRunnerTests
{
    [Fact]
    public async Task JobRunsAgainOnlyAfterItsInterval()
    {
        var job = new CountingJob();
        await using var host = await PlatformHost.CreateAsync(configure: services => services.AddSingleton<IScheduledJob>(job));
        var runner = host.Get<ScheduledJobRunner>();

        Assert.Equal(JobRunResult.Succeeded, await runner.RunIfDueAsync(job, force: false, TestContext.Current.CancellationToken));
        Assert.Equal(JobRunResult.NotDue, await runner.RunIfDueAsync(job, force: false, TestContext.Current.CancellationToken));

        host.Time.Advance(job.Interval);
        Assert.Equal(JobRunResult.Succeeded, await runner.RunIfDueAsync(job, force: false, TestContext.Current.CancellationToken));
        Assert.Equal(2, job.Runs);
    }

    [Fact]
    public async Task ForcedRunIgnoresTheInterval()
    {
        var job = new CountingJob();
        await using var host = await PlatformHost.CreateAsync();
        var runner = host.Get<ScheduledJobRunner>();

        await runner.RunIfDueAsync(job, force: false, TestContext.Current.CancellationToken);
        var result = await runner.RunIfDueAsync(job, force: true, TestContext.Current.CancellationToken);

        Assert.Equal(JobRunResult.Succeeded, result);
        Assert.Equal(2, job.Runs);
    }

    [Fact]
    public async Task JobDoesNotRunWhenAnotherReplicaHoldsTheLock()
    {
        var job = new CountingJob();
        await using var host = await PlatformHost.CreateAsync();
        await using var otherReplica = new NpgsqlConnection(host.Databases[0].ConnectionString);
        await otherReplica.OpenAsync(TestContext.Current.CancellationToken);
        await otherReplica.ExecuteAsync("select pg_advisory_lock(@key)", new { key = StableHash.Of($"reliquary.job.{job.Name}") });

        var result = await host.Get<ScheduledJobRunner>().RunIfDueAsync(job, force: true, TestContext.Current.CancellationToken);

        Assert.Equal(JobRunResult.Busy, result);
        Assert.Equal(0, job.Runs);
    }

    [Fact]
    public async Task FailedRunRecordsTheErrorAndRunsAgainAfterTheInterval()
    {
        var job = new CountingJob { Fail = true };
        await using var host = await PlatformHost.CreateAsync();
        var runner = host.Get<ScheduledJobRunner>();

        var result = await runner.RunIfDueAsync(job, force: false, TestContext.Current.CancellationToken);

        Assert.Equal(JobRunResult.Failed, result);
        await using var connection = new NpgsqlConnection(host.Databases[0].ConnectionString);
        var (status, error) = await connection.QuerySingleAsync<(string, string?)>(
            "select last_status, last_error from users.job_runs where name = @name", new { name = job.Name });
        Assert.Equal("failed", status);
        Assert.Equal("The job fails on purpose.", error);
    }

    private sealed class CountingJob : IScheduledJob
    {
        public string Name => "tests.counting";

        public TimeSpan Interval => TimeSpan.FromHours(1);

        public bool Fail { get; init; }

        public int Runs { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Runs++;
            return Fail ? throw new InvalidOperationException("The job fails on purpose.") : Task.CompletedTask;
        }
    }
}
