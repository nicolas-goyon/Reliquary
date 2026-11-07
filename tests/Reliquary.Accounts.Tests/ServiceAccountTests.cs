using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Accounts.ServiceAccounts;

namespace Reliquary.Accounts.Tests;

/// <summary>
/// The service accounts of the modules: creation by the administrator, the secret and the access to the internal API.
/// </summary>
public sealed class ServiceAccountTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AdministratorCreatesListsAndRevokesAServiceAccount()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        using var admin = await ServiceAccountSetup.SignInAdministratorAsync(instance, api);
        using var user = api.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, user, "plain@example.org");
        await user.SignInAsync("plain@example.org", AuthFlowTests.Password);

        var create = await admin.PostAsync("/api/v1/admin/service-accounts", new { name = "sync-module", description = "Synchronizes the decks." });
        var created = (await create.Content.ReadFromJsonAsync<CreatedServiceAccountResponse>(Cancel))!;
        var again = await admin.PostAsync("/api/v1/admin/service-accounts", new { name = "sync-module" });
        var badName = await admin.PostAsync("/api/v1/admin/service-accounts", new { name = "Sync Module" });
        var byUser = await user.PostAsync("/api/v1/admin/service-accounts", new { name = "user-module" });
        var listJson = await (await admin.GetAsync("/api/v1/admin/service-accounts")).Content.ReadAsStringAsync(Cancel);
        var version = await admin.GetAsync("/internal/v1", created.Secret);
        var revoke = await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/service-accounts/{created.ServiceAccount.Id}");
        var afterRevoke = await admin.GetAsync("/internal/v1", created.Secret);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.StartsWith($"rls_{created.ServiceAccount.Prefix}_", created.Secret, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(AccountErrorCodes.ServiceAccountNameTaken, await again.CodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byUser.StatusCode);
        Assert.Contains("sync-module", listJson, StringComparison.Ordinal);
        Assert.DoesNotContain(created.Secret, listJson, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Equal("1.1.0", (await version.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("coreApi").GetString());
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task ServiceAccountIsNotAUser()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var secret = await ServiceAccountSetup.CreateServiceAccountAsync(api, "not-a-user");
        using var client = api.CreateBrowser();

        var me = await client.GetAsync("/api/v1/me", secret);
        var delegations = await client.GetAsync("/api/v1/me/delegations", secret);
        var status = await client.GetAsync("/api/v1/status", secret);
        var forged = await client.GetAsync("/internal/v1", secret[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delegations.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    [Fact]
    public async Task ServiceAccountReadsTheEventCatalogAndAPublicProfile()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var secret = await ServiceAccountSetup.CreateServiceAccountAsync(api, "reader");
        using var user = api.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, user, "profile@example.org");
        var session = await user.SignInAsync("profile@example.org", AuthFlowTests.Password);

        var events = await (await user.GetAsync("/internal/v1/events/types", secret)).Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var profile = await user.GetAsync($"/internal/v1/users/{session.Account.Id}/profile", secret);
        var profileJson = await profile.Content.ReadAsStringAsync(Cancel);
        var unknown = await user.GetAsync($"/internal/v1/users/{Guid.NewGuid()}/profile", secret);

        Assert.Contains(events.GetProperty("items").EnumerateArray(), item => item.GetProperty("type").GetString() == "accounts.account.created");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Contains("\"displayName\":\"profile\"", profileJson, StringComparison.Ordinal);
        Assert.DoesNotContain("profile@example.org", profileJson, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}

/// <summary>
/// Creates the service accounts and the delegations of the tests.
/// </summary>
internal static class ServiceAccountSetup
{
    public static async Task<BrowserClient> SignInAdministratorAsync(AccountsInstance instance, AccountsApi api)
    {
        var admin = api.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, admin, "admin@example.org");
        await admin.SignInAsync("admin@example.org", AuthFlowTests.Password);
        return admin;
    }

    /// <summary>Creates a service account with the service of the CLI, and returns its secret.</summary>
    public static async Task<string> CreateServiceAccountAsync(AccountsApi api, string name)
    {
        var creation = await api.Services.GetRequiredService<ServiceAccountService>().CreateAsync(name, $"The module {name}.", TestContext.Current.CancellationToken);
        return creation.Created!.Secret;
    }

    public static async Task<Guid> ServiceAccountIdAsync(AccountsApi api, string name) =>
        (await api.Services.GetRequiredService<ServiceAccountService>().FindAsync(name, TestContext.Current.CancellationToken))!.Id;
}
