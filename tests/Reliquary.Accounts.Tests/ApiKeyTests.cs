using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Reliquary.Accounts.Data;
using Reliquary.Accounts.Profile;
using Reliquary.Accounts.Quotas;
using Reliquary.BuildingBlocks.Jobs;
using StackExchange.Redis;

namespace Reliquary.Accounts.Tests;

public sealed class ApiKeyTests
{
    [Fact]
    public async Task UserCreatesUsesListsAndRevokesAnApiKey()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, browser, "keys@example.org");
        await browser.SignInAsync("keys@example.org", "correct horse battery");

        var created = await (await browser.PostAsync("/api/v1/me/api-keys", new { name = "Script" }))
            .Content.ReadFromJsonAsync<CreatedApiKeyResponse>(TestContext.Current.CancellationToken);
        var withKey = await browser.GetAsync("/api/v1/me", created!.Key);
        var patchWithKey = await browser.SendAsync(HttpMethod.Patch, "/api/v1/me", new { language = "fr" }, created.Key);
        var keysWithKey = await browser.GetAsync("/api/v1/me/api-keys", created.Key);
        var list = await (await browser.GetAsync("/api/v1/me/api-keys")).Content.ReadFromJsonAsync<List<ApiKeyResponse>>(TestContext.Current.CancellationToken);
        var revoke = await browser.SendAsync(HttpMethod.Delete, $"/api/v1/me/api-keys/{created.Id}");
        var afterRevoke = await browser.GetAsync("/api/v1/me", created.Key);

        Assert.StartsWith($"rlq_{created.Prefix}_", created.Key, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patchWithKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, keysWithKey.StatusCode);
        var listed = Assert.Single(list!);
        Assert.Equal("Script", listed.Name);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task KeyWithAWrongSecretIsRefused()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, browser, "wrong@example.org");
        await browser.SignInAsync("wrong@example.org", "correct horse battery");
        var created = await (await browser.PostAsync("/api/v1/me/api-keys", new { name = "Script" }))
            .Content.ReadFromJsonAsync<CreatedApiKeyResponse>(TestContext.Current.CancellationToken);

        var forged = created!.Key[..^4] + "AAAA";
        var response = await browser.GetAsync("/api/v1/status", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AccountErrorCodes.InvalidCredentialsSent, await response.CodeAsync());
    }

    [Fact]
    public async Task QuotaIsSharedByTheReplicasAndTheFlushJobCopiesTheCounters()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        instance.Settings["Quotas:RequestsPerKeyPerDay"] = "3";
        var replicaA = instance.StartReplica();
        var replicaB = instance.StartReplica();
        using var browserA = replicaA.CreateBrowser();
        using var browserB = replicaB.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, browserA, "quota@example.org");
        await browserA.SignInAsync("quota@example.org", "correct horse battery");
        var key = (await (await browserA.PostAsync("/api/v1/me/api-keys", new { name = "Bot" }))
            .Content.ReadFromJsonAsync<CreatedApiKeyResponse>(TestContext.Current.CancellationToken))!;

        var first = await browserA.GetAsync("/api/v1/status", key.Key);
        await browserB.GetAsync("/api/v1/status", key.Key);
        await browserA.GetAsync("/api/v1/status", key.Key);
        var refused = await browserB.GetAsync("/api/v1/status", key.Key);
        var session = await browserA.GetAsync("/api/v1/status");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("3", first.Headers.GetValues("RateLimit-Limit").Single());
        Assert.Equal("2", first.Headers.GetValues("RateLimit-Remaining").Single());
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("quota_exceeded", await refused.CodeAsync());
        Assert.True(refused.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        var job = new UsageFlushJob(
            replicaA.Services.GetRequiredService<IConnectionMultiplexer>(),
            replicaA.Services.GetRequiredService<AccountStore>(),
            instance.Time,
            NullLogger<UsageFlushJob>.Instance);
        await job.RunAsync(new JobRun(Force: true), TestContext.Current.CancellationToken);
        await job.RunAsync(new JobRun(Force: true), TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(instance.Database.ConnectionString);
        var keyCount = await connection.ExecuteScalarAsync<long>(
            "select request_count from users.usage_counters where subject_kind = 'key' and subject_id = @id", new { id = key.Id });
        Assert.Equal(3, keyCount);
    }

    [Fact]
    public async Task AccountHasAMaximumNumberOfKeys()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, browser, "many@example.org");
        await browser.SignInAsync("many@example.org", "correct horse battery");

        for (var index = 0; index < ProfileEndpoints.MaxApiKeys; index++)
        {
            Assert.Equal(HttpStatusCode.Created, (await browser.PostAsync("/api/v1/me/api-keys", new { name = $"Key {index}" })).StatusCode);
        }

        var refused = await browser.PostAsync("/api/v1/me/api-keys", new { name = "One more" });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(AccountErrorCodes.ApiKeyLimit, await refused.CodeAsync());
    }
}
