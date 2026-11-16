using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Extensions.Catalog;

namespace Reliquary.Platform.Tests.Extensions.Catalog;

public sealed class PluginCatalogSyncTests : IDisposable
{
    private readonly TestCatalog catalog = new();

    private Dictionary<string, byte[]>? served;

    public void Dispose() => catalog.Dispose();

    [Fact]
    public async Task FirstSyncKeepsTheIndexAndTheFiles()
    {
        var files = catalog.AddVersion("hello", "1.0.0");
        await using var host = await CreateHostAsync();

        var result = await host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogIndexStatus.Valid, result.Status);
        Assert.Equal(["hello 1.0.0"], result.AddedVersions);
        var entry = await host.Get<PluginFileStore>().FindAsync("hello", "1.0.0", "dist/index.js", TestContext.Current.CancellationToken);
        Assert.Equal(files["dist/index.js"], entry!.Content);
        Assert.NotNull(await host.Get<PluginFileStore>().FindAsync("hello", "1.0.0", "manifest.json", TestContext.Current.CancellationToken));
        Assert.Equal((1L, catalog.Index().Length), await StoredIndexAsync(host));
    }

    [Fact]
    public async Task SyncOfTheSameIndexGetsNoFile()
    {
        catalog.AddVersion("hello", "1.0.0");
        var handler = Handler();
        await using var host = await CreateHostAsync(handler);
        var sync = host.Get<PluginCatalogSync>();
        await sync.SyncAsync(TestContext.Current.CancellationToken);
        var requests = handler.Requests.Count;

        var result = await sync.SyncAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogIndexStatus.Unchanged, result.Status);
        Assert.Empty(result.AddedVersions);
        Assert.Equal([TestCatalog.IndexUrl, TestCatalog.IndexUrl + ".sig"], handler.Requests.Skip(requests));
    }

    [Fact]
    public async Task NewSerialAddsOnlyTheNewVersion()
    {
        catalog.AddVersion("hello", "1.0.0");
        await using var host = await CreateHostAsync();
        var sync = host.Get<PluginCatalogSync>();
        await sync.SyncAsync(TestContext.Current.CancellationToken);

        catalog.Serial = 2;
        catalog.AddVersion("hello", "1.1.0");
        served = null;
        var result = await sync.SyncAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogIndexStatus.Valid, result.Status);
        Assert.Equal(["hello 1.1.0"], result.AddedVersions);
        Assert.Equal(2L, (await StoredIndexAsync(host)).Serial);
    }

    [Fact]
    public async Task FileWithAnotherHashIsNotKeptAndTheNextSyncTriesAgain()
    {
        catalog.AddVersion("hello", "1.0.0");
        catalog.AddVersion("other", "2.0.0");
        await using var host = await CreateHostAsync();
        var store = host.Get<PluginFileStore>();
        var url = TestCatalog.FileUrl("hello", "1.0.0", "dist/index.js");
        Served()[url] = Encoding.UTF8.GetBytes("alert('changed');\n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken));

        Assert.Contains("hello 1.0.0", exception.Message, StringComparison.Ordinal);
        Assert.False(await store.HasVersionAsync("hello", "1.0.0", TestContext.Current.CancellationToken));
        Assert.True(await store.HasVersionAsync("other", "2.0.0", TestContext.Current.CancellationToken));
        Assert.Equal(1L, (await StoredIndexAsync(host)).Serial);

        served = null;
        var result = await host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["hello 1.0.0"], result.AddedVersions);
    }

    [Fact]
    public async Task FileLargerThanItsSizeIsNotKept()
    {
        catalog.AddVersion("hello", "1.0.0");
        await using var host = await CreateHostAsync();
        var url = TestCatalog.FileUrl("hello", "1.0.0", "dist/index.js");
        Served()[url] = [.. Served()[url], .. "//"u8.ToArray()];

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken));

        Assert.False(await host.Get<PluginFileStore>().HasVersionAsync("hello", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManifestOfAnotherPluginIsNotKept()
    {
        catalog.AddVersion("hello", "1.0.0", ("id", "other"));
        await using var host = await CreateHostAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken));

        Assert.False(await host.Get<PluginFileStore>().HasVersionAsync("hello", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManifestWithoutItsEntryFileIsNotKept()
    {
        catalog.AddVersion("hello", "1.0.0", ("entry", "dist/main.js"));
        await using var host = await CreateHostAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken));

        Assert.False(await host.Get<PluginFileStore>().HasVersionAsync("hello", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokedVersionIsNotDownloaded()
    {
        catalog.AddVersion("hello", "1.0.0");
        catalog.Revoked.Add(new CatalogRevocation("hello", "1.0.0", "Test revocation."));
        var handler = Handler();
        await using var host = await CreateHostAsync(handler);

        var result = await host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.AddedVersions);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OlderIndexIsRefusedAndTheInstanceKeepsTheLastIndex()
    {
        catalog.AddVersion("hello", "1.0.0");
        catalog.Serial = 5;
        await using var host = await CreateHostAsync();
        var sync = host.Get<PluginCatalogSync>();
        await sync.SyncAsync(TestContext.Current.CancellationToken);
        var stored = await StoredIndexAsync(host);

        catalog.Serial = 4;
        served = null;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SyncAsync(TestContext.Current.CancellationToken));

        Assert.Contains(nameof(CatalogIndexStatus.Rollback), exception.Message, StringComparison.Ordinal);
        Assert.Equal(stored, await StoredIndexAsync(host));
    }

    [Fact]
    public async Task IndexWithABadSignatureIsRefused()
    {
        catalog.AddVersion("hello", "1.0.0");
        await using var host = await CreateHostAsync();
        Served()[TestCatalog.IndexUrl + ".sig"] = catalog.Sign("other"u8.ToArray());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken));

        Assert.Contains(nameof(CatalogIndexStatus.BadSignature), exception.Message, StringComparison.Ordinal);
        Assert.Null(await StoredIndexOrNullAsync(host));
        Assert.False(await host.Get<PluginFileStore>().HasVersionAsync("hello", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://catalog.test/plugins/index.json")]
    [InlineData("ftp://catalog.test/plugins/index.json")]
    public async Task CatalogAddressMustBeHttps(string? url)
    {
        await using var host = await CreateHostAsync(url: url ?? string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstanceWithNoTrustedKeyDoesNotSync()
    {
        await using var host = await CreateHostAsync(trustKey: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Get<PluginCatalogSync>().SyncAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Plugins:Catalog:Keys", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerHasTheJobOnlyWithACatalogAddress()
    {
        static IReadOnlyList<string> Jobs(string? url)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Plugins:Catalog:Url"] = url })
                .Build();
            var services = new ServiceCollection();
            services.AddReliquaryWorkerPlatform(configuration);
            return services.Where(service => service.ServiceType == typeof(IScheduledJob)).Select(service => service.ImplementationType!.Name).ToList();
        }

        Assert.Empty(Jobs(null));
        Assert.Equal([nameof(PluginCatalogJob)], Jobs(TestCatalog.IndexUrl));
    }

    private Dictionary<string, byte[]> Served() => served ??= catalog.Files();

    private TestCatalogHandler Handler() => new(() => Served());

    private Task<PlatformHost> CreateHostAsync(TestCatalogHandler? handler = null, string url = TestCatalog.IndexUrl, bool trustKey = true)
    {
        handler ??= Handler();
        return PlatformHost.CreateAsync(configure: services =>
        {
            services.Configure<PluginCatalogOptions>(options =>
            {
                options.Url = url;
                if (trustKey)
                {
                    options.Keys.Add(catalog.PublicKey);
                }
            });
            services.AddHttpClient(PluginCatalogSync.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        });
    }

    private static async Task<(long Serial, int Length)> StoredIndexAsync(PlatformHost host) =>
        await StoredIndexOrNullAsync(host) ?? throw new InvalidOperationException("The instance has no catalog index.");

    private static async Task<(long Serial, int Length)?> StoredIndexOrNullAsync(PlatformHost host)
    {
        await using var connection = new NpgsqlConnection(host.Databases[0].ConnectionString);
        var rows = await connection.QueryAsync<(long Serial, int Length)>("select serial, length(index_content) from users.plugin_catalog");
        return rows.Cast<(long Serial, int Length)?>().SingleOrDefault();
    }
}
