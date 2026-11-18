using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Extensions.Catalog;
using Reliquary.Platform.Tests.Extensions.Catalog;

namespace Reliquary.Api.Tests.Plugins;

/// <summary>
/// The plugin catalog for the administrators: the list, a sync and the installation of a version. See issue #300.
/// </summary>
public sealed class PluginCatalogAdminTests
{
    private const string CatalogPath = "/api/v1/admin/plugin-catalog";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AdministratorSyncsTheCatalogAndInstallsTheLastVersion()
    {
        using var catalog = new TestCatalog();
        var id = PluginInstance.NewId();
        catalog.AddVersion(id, "1.0.0", ("name", "Deck statistics"), ("description", "Old text."));
        catalog.AddVersion(id, "1.10.0", ("name", "Deck statistics"), ("description", "Charts for a deck."));
        catalog.AddVersion(id, "1.2.0", ("name", "Deck statistics"));
        await using var instance = await PluginRevocationTests.CreateInstanceAsync(catalog);
        var admin = await instance.AdministratorAsync();

        var before = await admin.GetFromJsonAsync<PluginCatalogResponse>(CatalogPath, Cancel);
        var sync = await admin.PostAsync($"{CatalogPath}/sync", null, Cancel);
        var synced = await sync.Content.ReadFromJsonAsync<CatalogSyncResponse>(Cancel);
        var listed = Assert.Single((await admin.GetFromJsonAsync<PluginCatalogResponse>(CatalogPath, Cancel))!.Plugins);
        var install = await admin.PostAsync($"{CatalogPath}/{id}/1.10.0/install", null, Cancel);
        var installed = await install.Content.ReadFromJsonAsync<AdminExtensionResponse>(Cancel);
        var again = await admin.PostAsync($"{CatalogPath}/{id}/1.10.0/install", null, Cancel);
        var missing = await admin.PostAsync($"{CatalogPath}/{id}/9.9.9/install", null, Cancel);

        Assert.True(before!.Configured);
        Assert.Null(before.Serial);
        Assert.Empty(before.Plugins);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        Assert.Equal(1, synced!.Serial);
        Assert.Equal(3, synced.AddedVersions.Count);
        Assert.Equal((id, "Deck statistics", "Charts for a deck.", "1.10.0"), (listed.Id, listed.Name, listed.Description, listed.LatestVersion));
        Assert.Equal(["1.10.0", "1.2.0", "1.0.0"], listed.Versions.Select(version => version.Version));
        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        Assert.Equal("1.10.0", installed!.Extension.Version);
        Assert.False(installed.Extension.Enabled);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task RevokedVersionIsNotTheLastVersionAndDoesNotInstall()
    {
        using var catalog = new TestCatalog();
        var id = PluginInstance.NewId();
        catalog.AddVersion(id, "1.0.0");
        catalog.AddVersion(id, "2.0.0");
        await using var instance = await PluginRevocationTests.CreateInstanceAsync(catalog);
        var admin = await instance.AdministratorAsync();
        (await admin.PostAsync($"{CatalogPath}/sync", null, Cancel)).EnsureSuccessStatusCode();

        catalog.Serial = 2;
        catalog.Revoked.Add(new CatalogRevocation(id, "2.0.0", "The version does not start."));
        var sync = await (await admin.PostAsync($"{CatalogPath}/sync", null, Cancel)).Content.ReadFromJsonAsync<CatalogSyncResponse>(Cancel);
        var listed = Assert.Single((await admin.GetFromJsonAsync<PluginCatalogResponse>(CatalogPath, Cancel))!.Plugins);
        var install = await admin.PostAsync($"{CatalogPath}/{id}/2.0.0/install", null, Cancel);

        Assert.Equal([$"{id} 2.0.0"], sync!.RevokedVersions);
        Assert.Equal("1.0.0", listed.LatestVersion);
        Assert.Equal("The version does not start.", listed.Versions[0].RevocationReason);
        Assert.Null(listed.Versions[1].RevocationReason);
        Assert.Equal(HttpStatusCode.Conflict, install.StatusCode);
        Assert.Equal(ExtensionEndpoints.VersionRevoked, (await install.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task OnlyAnAdministratorUsesTheCatalogAndAnInstanceWithoutCatalogDoesNotSync()
    {
        await using var instance = await PluginInstance.CreateAsync();
        var admin = await instance.AdministratorAsync();
        using var user = await instance.SignInAsync("reader@example.org");

        var forbidden = await user.GetAsync(CatalogPath, Cancel);
        var list = await admin.GetFromJsonAsync<PluginCatalogResponse>(CatalogPath, Cancel);
        var sync = await admin.PostAsync($"{CatalogPath}/sync", null, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.False(list!.Configured);
        Assert.Equal(HttpStatusCode.Conflict, sync.StatusCode);
        Assert.Equal(PluginCatalogEndpoints.CatalogNotConfigured, (await sync.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
    }
}
