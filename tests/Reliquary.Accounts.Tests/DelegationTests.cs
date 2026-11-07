using System.Net;
using System.Net.Http.Json;
using Reliquary.Accounts.ServiceAccounts;

namespace Reliquary.Accounts.Tests;

/// <summary>
/// The delegations of the users and the delegation tokens of the service accounts.
/// See docs/architecture/service-accounts.md.
/// </summary>
public sealed class DelegationTests
{
    private static readonly string[] ProfileRead = ["profile:read"];
    private static readonly string[] DecksRead = ["decks:read"];
    private static readonly string[] DecksWrite = ["decks:write"];
    private static readonly string[] UnknownScope = ["decks:admin"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ServiceAccountActsForTheUserWithTheScopesOfTheToken()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var secret = await ServiceAccountSetup.CreateServiceAccountAsync(api, "profile-reader");
        var serviceAccountId = await ServiceAccountSetup.ServiceAccountIdAsync(api, "profile-reader");
        using var user = api.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, user, "delegate@example.org");
        var session = await user.SignInAsync("delegate@example.org", AuthFlowTests.Password);

        var choices = await (await user.GetAsync("/api/v1/service-accounts")).Content.ReadFromJsonAsync<List<ServiceAccountSummary>>(Cancel);
        var create = await user.PostAsync("/api/v1/me/delegations", new { serviceAccountId, scopes = ProfileRead, expiresInDays = 30 });
        var delegation = (await create.Content.ReadFromJsonAsync<DelegationResponse>(Cancel))!;
        var listed = await (await user.GetAsync("/api/v1/me/delegations")).Content.ReadFromJsonAsync<List<DelegationResponse>>(Cancel);
        var forModule = await (await user.GetAsync("/internal/v1/delegations", secret)).Content.ReadFromJsonAsync<List<InternalDelegationResponse>>(Cancel);
        var token = await IssueAsync(user, secret, delegation.Id);
        var tooWide = await user.SendAsync(HttpMethod.Post, $"/internal/v1/delegations/{delegation.Id}/tokens", new { scopes = DecksWrite }, secret);

        var allowed = await user.GetAsync("/api/v1/me", token.Token);
        var sessionOnly = await user.SendAsync(HttpMethod.Patch, "/api/v1/me", new { language = "fr" }, token.Token);
        var missingScope = await user.GetAsync("/api/v1/me/api-keys", token.Token);
        var noScopeEndpoint = await user.GetAsync("/api/v1/status", token.Token);
        var internalApi = await user.GetAsync("/internal/v1", token.Token);

        Assert.Contains(choices!, choice => choice.Id == serviceAccountId && choice.Name == "profile-reader");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(["profile:read"], Assert.Single(listed!).Scopes);
        Assert.Equal(TimeSpan.FromDays(30), delegation.ExpiresAt - delegation.CreatedAt);
        Assert.Equal(session.Account.Id, Assert.Single(forModule!).AccountId);
        Assert.StartsWith("rld_", token.Token, StringComparison.Ordinal);
        Assert.Equal(["profile:read"], token.Scopes);
        Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains("delegate@example.org", await allowed.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, sessionOnly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingScope.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noScopeEndpoint.StatusCode);
        Assert.Equal(AccountErrorCodes.ScopeMissing, await noScopeEndpoint.CodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, internalApi.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutTheScopeOfTheEndpointIsRefused()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var (user, secret, delegationId) = await DelegateAsync(instance, api, "scopes@example.org", "decks:read", "profile:read");
        using var _ = user;

        var token = await IssueAsync(user, secret, delegationId, "decks:read");
        var response = await user.GetAsync("/api/v1/me", token.Token);

        Assert.Equal(["decks:read"], token.Scopes);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrorCodes.ScopeMissing, await response.CodeAsync());
    }

    [Fact]
    public async Task RevokedDelegationStopsItsTokens()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var (user, secret, delegationId) = await DelegateAsync(instance, api, "revoke@example.org", "profile:read");
        using var _ = user;
        var token = await IssueAsync(user, secret, delegationId);
        var before = await user.GetAsync("/api/v1/me", token.Token);

        var revoke = await user.SendAsync(HttpMethod.Delete, $"/api/v1/me/delegations/{delegationId}");
        var after = await user.GetAsync("/api/v1/me", token.Token);
        var newToken = await user.SendAsync(HttpMethod.Post, $"/internal/v1/delegations/{delegationId}/tokens", new { }, secret);
        var listed = await (await user.GetAsync("/api/v1/me/delegations")).Content.ReadFromJsonAsync<List<DelegationResponse>>(Cancel);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, newToken.StatusCode);
        Assert.Empty(listed!);
    }

    [Fact]
    public async Task ExpiredTokenAndExpiredDelegationAreRefused()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var (user, secret, delegationId) = await DelegateAsync(instance, api, "expire@example.org", "profile:read");
        using var _ = user;
        var token = await IssueAsync(user, secret, delegationId);

        instance.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        var expiredToken = await user.GetAsync("/api/v1/me", token.Token);
        var fresh = await IssueAsync(user, secret, delegationId);
        var freshResponse = await user.GetAsync("/api/v1/me", fresh.Token);

        instance.Time.Advance(TimeSpan.FromDays(90));
        var expiredDelegation = await user.GetAsync("/api/v1/me", fresh.Token);
        var refused = await user.SendAsync(HttpMethod.Post, $"/internal/v1/delegations/{delegationId}/tokens", new { }, secret);
        var forModule = await (await user.GetAsync("/internal/v1/delegations", secret)).Content.ReadFromJsonAsync<List<InternalDelegationResponse>>(Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, expiredToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, freshResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredDelegation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Empty(forModule!);
    }

    [Fact]
    public async Task RevokedServiceAccountStopsTheTokensOfItsDelegations()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var (user, secret, delegationId) = await DelegateAsync(instance, api, "module-revoked@example.org", "profile:read");
        using var _ = user;
        var token = await IssueAsync(user, secret, delegationId);
        using var admin = await ServiceAccountSetup.SignInAdministratorAsync(instance, api);

        var revoke = await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/service-accounts/{await ServiceAccountSetup.ServiceAccountIdAsync(api, "module")}");
        var after = await user.GetAsync("/api/v1/me", token.Token);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task OnlyTheUserWithASessionGivesADelegationToAnActiveServiceAccount()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var api = instance.StartReplica();
        var (user, _, delegationId) = await DelegateAsync(instance, api, "rules@example.org", "decks:read");
        using var __ = user;
        var otherSecret = await ServiceAccountSetup.CreateServiceAccountAsync(api, "other-module");
        var serviceAccountId = await ServiceAccountSetup.ServiceAccountIdAsync(api, "module");
        var key = (await (await user.PostAsync("/api/v1/me/api-keys", new { name = "Script" })).Content.ReadFromJsonAsync<Profile.CreatedApiKeyResponse>(Cancel))!;

        var withKey = await user.SendAsync(HttpMethod.Post, "/api/v1/me/delegations", new { serviceAccountId, scopes = DecksRead }, key.Key);
        var unknownScope = await user.PostAsync("/api/v1/me/delegations", new { serviceAccountId, scopes = UnknownScope });
        var noScope = await user.PostAsync("/api/v1/me/delegations", new { serviceAccountId, scopes = Array.Empty<string>() });
        var tooLong = await user.PostAsync("/api/v1/me/delegations", new { serviceAccountId, scopes = DecksRead, expiresInDays = 400 });
        var unknownAccount = await user.PostAsync("/api/v1/me/delegations", new { serviceAccountId = Guid.NewGuid(), scopes = DecksRead });
        var otherModule = await user.SendAsync(HttpMethod.Post, $"/internal/v1/delegations/{delegationId}/tokens", new { }, otherSecret);

        Assert.Equal(HttpStatusCode.Forbidden, withKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherModule.StatusCode);
    }

    /// <summary>
    /// Creates the service account "module" when it does not exist, a user and a delegation of the user to the service account.
    /// </summary>
    private static async Task<(BrowserClient User, string Secret, Guid DelegationId)> DelegateAsync(AccountsInstance instance, AccountsApi api, string email, params string[] scopes)
    {
        var secret = await ServiceAccountSetup.CreateServiceAccountAsync(api, "module");
        var serviceAccountId = await ServiceAccountSetup.ServiceAccountIdAsync(api, "module");
        var user = api.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, user, email);
        await user.SignInAsync(email, AuthFlowTests.Password);
        var create = await user.PostAsync("/api/v1/me/delegations", new { serviceAccountId, scopes });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var delegation = (await create.Content.ReadFromJsonAsync<DelegationResponse>(Cancel))!;
        return (user, secret, delegation.Id);
    }

    private static async Task<DelegationTokenResponse> IssueAsync(BrowserClient client, string secret, Guid delegationId, params string[] scopes)
    {
        var response = await client.SendAsync(HttpMethod.Post, $"/internal/v1/delegations/{delegationId}/tokens", new { scopes = scopes.Length == 0 ? null : scopes }, secret);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DelegationTokenResponse>(Cancel))!;
    }
}
