using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using Reliquary.Accounts.Auth;
using Reliquary.Accounts.Profile;

namespace Reliquary.Accounts.Tests;

public sealed class AuthFlowTests
{
    [Fact]
    public async Task DevelopmentSignInMakesTheAccountOneTimeAndStartsASession()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        var first = await browser.SignInAsync("Ana@Example.org", "Ana", "fr");
        var cookie = browser.RefreshCookie;
        var second = await browser.SignInAsync(" ana@example.org ", "Other name", "en");
        var me = await browser.GetAsync("/api/v1/me");
        var profile = await me.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(cookie);
        Assert.Equal(first.Account.Id, second.Account.Id);
        Assert.Equal("Ana", profile!.Account.DisplayName);
        Assert.Equal("fr", profile.Account.Language);
        Assert.Equal("Ana@Example.org", profile.Account.Email);
        Assert.False(profile.Account.IsAdministrator);
    }

    [Fact]
    public async Task DevelopmentSignInUsesDefaultsAndChecksTheFields()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        var session = await browser.SignInAsync("defaults@example.org");
        var response = await browser.PostAsync("/api/v1/auth/development/sign-in", new { email = "not an address", language = "de" });
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("defaults", session.Account.DisplayName);
        Assert.Equal("en", session.Account.Language);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"field\":\"email\"", body, StringComparison.Ordinal);
        Assert.Contains("\"field\":\"language\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstanceWithoutTheSettingHasNoDevelopmentSignInAndNoPasswordEndpoints()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        instance.Settings["Auth:AllowDevelopmentSignIn"] = "false";
        using var browser = instance.StartReplica().CreateBrowser();

        var signIn = await browser.PostAsync("/api/v1/auth/development/sign-in", new { email = "ana@example.org" });
        var providers = await browser.GetAsync("/api/v1/auth/providers");
        var list = await providers.Content.ReadFromJsonAsync<ProvidersResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, signIn.StatusCode);
        Assert.False(list!.DevelopmentSignIn);
        Assert.Empty(list.Providers);
        foreach (var path in new[] { "/api/v1/auth/register", "/api/v1/auth/sign-in", "/api/v1/auth/password/forgot", "/api/v1/auth/password/reset", "/api/v1/auth/email-check" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync(path, new { email = "ana@example.org", password = "correct horse battery" })).StatusCode);
        }
    }

    [Fact]
    public async Task NewAccountGetsTheShardAssignmentAndTheEvent()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        await browser.SignInAsync("event@example.org");

        await using var connection = new NpgsqlConnection(instance.Database.ConnectionString);
        var accountId = await connection.ExecuteScalarAsync<Guid>("select id from users.accounts where email_normalized = 'event@example.org'");
        var shard = await connection.ExecuteScalarAsync<string>("select shard from users.shard_directory where owner_id = @accountId", new { accountId });
        var eventType = await connection.ExecuteScalarAsync<string>("select event_type from users.outbox where owner_id = @accountId", new { accountId });
        Assert.NotNull(shard);
        Assert.Equal("accounts.account.created", eventType);
    }

    [Fact]
    public async Task AdministratorAddressGivesTheAdministratorRole()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        var session = await browser.SignInAsync("admin@example.org");

        Assert.True(session.Account.IsAdministrator);
    }

    [Fact]
    public async Task RefreshReplacesTheCookieAndAllowsTheGracePeriod()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await browser.SignInAsync("refresh@example.org");
        var first = browser.RefreshCookie;

        var refreshed = await browser.PostAsync("/api/v1/auth/refresh");
        var second = browser.RefreshCookie;

        // A second tab sends the old cookie at the same time: it gets an access token, and the cookie does not change.
        browser.RefreshCookie = first;
        var concurrent = await browser.PostAsync("/api/v1/auth/refresh");

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.OK, concurrent.StatusCode);
        Assert.False(concurrent.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ReuseOfAnOldRefreshTokenStopsTheSession()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await browser.SignInAsync("reuse@example.org");
        var stolen = browser.RefreshCookie;
        await browser.PostAsync("/api/v1/auth/refresh");
        var current = browser.RefreshCookie;

        instance.Time.Advance(AccountService.RefreshGracePeriod + TimeSpan.FromSeconds(1));
        browser.RefreshCookie = stolen;
        var reuse = await browser.PostAsync("/api/v1/auth/refresh");
        browser.RefreshCookie = current;
        var afterReuse = await browser.PostAsync("/api/v1/auth/refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(AccountErrorCodes.SessionExpired, await reuse.CodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task SignOutStopsTheSession()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await browser.SignInAsync("out@example.org");
        var cookie = browser.RefreshCookie;

        var signOut = await browser.PostAsync("/api/v1/auth/sign-out");
        browser.RefreshCookie = cookie;
        var refresh = await browser.PostAsync("/api/v1/auth/refresh");

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task SessionStartedOnOneReplicaWorksOnAnotherReplica()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var replicaA = instance.StartReplica();
        var replicaB = instance.StartReplica();
        using var browserA = replicaA.CreateBrowser();
        using var browserB = replicaB.CreateBrowser();
        await browserA.SignInAsync("replicas@example.org");

        browserB.RefreshCookie = browserA.RefreshCookie;
        var refresh = await browserB.PostAsync("/api/v1/auth/refresh");
        var session = await refresh.Content.ReadFromJsonAsync<SessionResponse>(TestContext.Current.CancellationToken);
        var me = await browserA.GetAsync("/api/v1/me", session!.AccessToken);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task UserChangesTheProfileAndThePasswordEndpointDoesNotExist()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await browser.SignInAsync("profile@example.org");

        var patch = await browser.SendAsync(HttpMethod.Patch, "/api/v1/me", new { displayName = "  New name ", language = "fr" });
        var account = await patch.Content.ReadFromJsonAsync<AccountResponse>(TestContext.Current.CancellationToken);
        var password = await browser.PostAsync("/api/v1/me/password", new { newPassword = "the next password" });

        Assert.Equal("New name", account!.DisplayName);
        Assert.Equal("fr", account.Language);
        Assert.Equal(HttpStatusCode.NotFound, password.StatusCode);
    }

    [Fact]
    public async Task StartOfAnExternalSignInHasARateLimit()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        instance.Settings["Auth:RequestsPerMinute"] = "2";
        using var browser = instance.StartReplica().CreateBrowser();

        await browser.GetAsync("/api/v1/auth/external/google");
        await browser.GetAsync("/api/v1/auth/external/google");
        var third = await browser.GetAsync("/api/v1/auth/external/google");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("rate_limited", await third.CodeAsync());
    }

    [Fact]
    public async Task ProtectedEndpointWithoutTokenGivesAuthenticationRequired()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        var response = await browser.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("authentication_required", await response.CodeAsync());
    }

    [Fact]
    public async Task IncorrectTokenOnAPublicEndpointGivesAnError()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        var response = await browser.GetAsync("/api/v1/status", "not.a.token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AccountErrorCodes.InvalidCredentialsSent, await response.CodeAsync());
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/decks/1", "/decks/1")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    public void ReturnUrlMustBeALocalPath(string? returnUrl, string expected) =>
        Assert.Equal(expected, ExternalEndpoints.SafeReturnUrl(returnUrl));
}
