using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Testing;

namespace Reliquary.Api.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task ReadinessIsHealthyWhenPostgresAndRedisAnswer()
    {
        await using var database = await TestServers.CreateDatabaseAsync("api");
        await using var factory = NewFactory(database.ConnectionString, TestServers.Redis);
        await RequireRedisAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", report.RootElement.GetProperty("checks").GetProperty("postgresql").GetProperty("status").GetString());
        Assert.Equal("Healthy", report.RootElement.GetProperty("checks").GetProperty("redis").GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReadinessFailsWhenPostgresDoesNotAnswer()
    {
        await using var factory = NewFactory("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2", TestServers.Redis);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", report.RootElement.GetProperty("checks").GetProperty("postgresql").GetProperty("status").GetString());
    }

    [Fact]
    public async Task LivenessDoesNotDependOnTheDatabase()
    {
        await using var factory = NewFactory("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2", "127.0.0.1:1");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequestTraceIncludesTheDatabaseCall()
    {
        await using var database = await TestServers.CreateDatabaseAsync("api");
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Microsoft.AspNetCore" or "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var factory = NewFactory(database.ConnectionString, TestServers.Redis);
        factory.TestEndpoints = endpoints => endpoints.MapGet("/test/database", async (IDatabase db, CancellationToken cancellationToken) =>
        {
            await using var connection = await db.OpenUsersAsync(cancellationToken);
            return await connection.ExecuteScalarAsync<int>("select 1");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/test/database", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(activities, activity => activity.Source.Name == "Microsoft.AspNetCore" && activity.DisplayName.Contains("/test/database", StringComparison.Ordinal));
        Assert.Contains(activities, activity => activity.Source.Name == "Npgsql" && activity.TraceId == request.TraceId);
    }

    [Fact]
    public async Task MetricsEndpointServesPrometheusText()
    {
        await using var factory = NewFactory("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none", "127.0.0.1:1");
        using var client = factory.CreateClient();
        await client.GetAsync(new Uri("/api/v1/status", UriKind.Relative), TestContext.Current.CancellationToken);

        var response = await client.GetAsync(new Uri("/metrics", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http_server_request_duration", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private static ApiFactory NewFactory(string usersConnectionString, string redis)
    {
        var factory = new ApiFactory();
        factory.Settings["ConnectionStrings:Users"] = usersConnectionString;
        factory.Settings["ConnectionStrings:Redis"] = redis;
        return factory;
    }

    private static async Task RequireRedisAsync()
    {
        try
        {
            await using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(TestServers.Redis);
            await redis.GetDatabase().PingAsync();
        }
        catch (StackExchange.Redis.RedisConnectionException exception)
        {
            TestServers.Unavailable("Redis", exception);
        }
    }
}
