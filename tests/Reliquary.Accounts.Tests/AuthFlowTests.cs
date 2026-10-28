using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using Reliquary.Accounts.Auth;
using Reliquary.Accounts.Profile;

namespace Reliquary.Accounts.Tests;

public sealed class AuthFlowTests
{
    internal const string Password = "correct horse battery";

    [Fact]
    public async Task UserRegistersChecksTheEmailAndSignsIn()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        var register = await browser.PostAsync("/api/v1/auth/register", new { email = "Ana@Example.org", password = Password, displayName = "Ana", language = "fr" });
        var early = await browser.PostAsync("/api/v1/auth/sign-in", new { email = "ana@example.org", password = Password });
        var email = Assert.Single(instance.Emails.Messages);
        var check = await browser.PostAsync("/api/v1/auth/email-check", new { token = instance.Emails.LastToken("Ana@Example.org") });
        var session = await browser.SignInAsync("ana@example.org", Password);
        var me = await browser.Http.SendAsync(Authorized(HttpMethod.Get, "/api/v1/me", session.AccessToken), TestContext.Current.CancellationToken);
        var profile = await me.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Equal(AccountErrorCodes.EmailNotChecked, await early.CodeAsync());
        Assert.StartsWith("Vérifiez", email.Subject, StringComparison.Ordinal);
        Assert.Contains("https://reliquary.test/check-email?token=", email.TextBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
        Assert.NotNull(browser.RefreshCookie);
        Assert.Equal("Ana", profile!.Account.DisplayName);
        Assert.Equal("fr", profile.Account.Language);
        Assert.True(profile.Account.EmailChecked);
        Assert.False(profile.Account.IsAdministrator);
    }

    [Fact]
    public async Task RegistrationCreatesTheShardAssignmentAndTheEvent()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        await browser.PostAsync("/api/v1/auth/register", new { email = "event@example.org", password = Password, displayName = "Event", language = "en" });

        await using var connection = new NpgsqlConnection(instance.Database.ConnectionString);
        var accountId = await connection.ExecuteScalarAsync<Guid>("select id from users.accounts where email_normalized = 'event@example.org'");
        var shard = await connection.ExecuteScalarAsync<string>("select shard from users.shard_directory where owner_id = @accountId", new { accountId });
        var eventType = await connection.ExecuteScalarAsync<string>("select event_type from users.outbox where owner_id = @accountId", new { accountId });
        Assert.NotNull(shard);
        Assert.Equal("accounts.account.created", eventType);
    }

    [Fact]
    public async Task SecondRegistrationWithTheSameAddressSendsAWarningEmail()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        await browser.PostAsync("/api/v1/auth/register", new { email = "twice@example.org", password = Password, displayName = "One", language = "en" });
        var second = await browser.PostAsync("/api/v1/auth/register", new { email = "TWICE@example.org", password = "another password", displayName = "Two", language = "en" });

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(2, instance.Emails.Messages.Count);
        Assert.Equal("Your Reliquary account", instance.Emails.Messages[1].Subject);
    }

    [Fact]
    public async Task RegistrationWithIncorrectFieldsGivesAllTheFieldErrors()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();

        var response = await browser.PostAsync("/api/v1/auth/register", new { email = "not an address", password = "short", language = "de" });
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"field\":\"email\"", body, StringComparison.Ordinal);
        Assert.Contains("\"field\":\"password\"", body, StringComparison.Ordinal);
        Assert.Contains("\"field\":\"displayName\"", body, StringComparison.Ordinal);
        Assert.Contains("\"field\":\"language\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownAddressGiveTheSameError()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await RegisterCheckedAsync(instance, browser, "known@example.org");

        var wrong = await browser.PostAsync("/api/v1/auth/sign-in", new { email = "known@example.org", password = "wrong password" });
        var unknown = await browser.PostAsync("/api/v1/auth/sign-in", new { email = "unknown@example.org", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(AccountErrorCodes.InvalidCredentials, await wrong.CodeAsync());
        Assert.Equal(AccountErrorCodes.InvalidCredentials, await unknown.CodeAsync());
    }

    [Fact]
    public async Task AdministratorAddressGivesTheAdministratorRole()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await RegisterCheckedAsync(instance, browser, "admin@example.org");

        var session = await browser.SignInAsync("admin@example.org", Password);

        Assert.True(session.Account.IsAdministrator);
    }

    [Fact]
    public async Task RefreshReplacesTheCookieAndAllowsTheGracePeriod()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await RegisterCheckedAsync(instance, browser, "refresh@example.org");
        await browser.SignInAsync("refresh@example.org", Password);
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
        await RegisterCheckedAsync(instance, browser, "reuse@example.org");
        await browser.SignInAsync("reuse@example.org", Password);
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
        await RegisterCheckedAsync(instance, browser, "out@example.org");
        await browser.SignInAsync("out@example.org", Password);
        var cookie = browser.RefreshCookie;

        var signOut = await browser.PostAsync("/api/v1/auth/sign-out");
        browser.RefreshCookie = cookie;
        var refresh = await browser.PostAsync("/api/v1/auth/refresh");

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task PasswordResetSetsTheNewPasswordAndStopsTheSessions()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await RegisterCheckedAsync(instance, browser, "reset@example.org");
        await browser.SignInAsync("reset@example.org", Password);
        var oldCookie = browser.RefreshCookie;

        var forgot = await browser.PostAsync("/api/v1/auth/password/forgot", new { email = "reset@example.org" });
        var unknown = await browser.PostAsync("/api/v1/auth/password/forgot", new { email = "nobody@example.org" });
        var token = instance.Emails.LastToken("reset@example.org");
        var reset = await browser.PostAsync("/api/v1/auth/password/reset", new { token, password = "a new password" });
        var again = await browser.PostAsync("/api/v1/auth/password/reset", new { token, password = "a third password" });
        var refresh = await browser.PostAsync("/api/v1/auth/refresh");
        var oldPassword = await browser.PostAsync("/api/v1/auth/sign-in", new { email = "reset@example.org", password = Password });

        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(AccountErrorCodes.InvalidToken, await again.CodeAsync());
        Assert.NotNull(oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        await browser.SignInAsync("reset@example.org", "a new password");
    }

    [Fact]
    public async Task SessionStartedOnOneReplicaWorksOnAnotherReplica()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var replicaA = instance.StartReplica();
        var replicaB = instance.StartReplica();
        using var browserA = replicaA.CreateBrowser();
        using var browserB = replicaB.CreateBrowser();
        await RegisterCheckedAsync(instance, browserA, "replicas@example.org");
        await browserA.SignInAsync("replicas@example.org", Password);

        browserB.RefreshCookie = browserA.RefreshCookie;
        var refresh = await browserB.PostAsync("/api/v1/auth/refresh");
        var session = await refresh.Content.ReadFromJsonAsync<SessionResponse>(TestContext.Current.CancellationToken);
        var me = await browserA.GetAsync("/api/v1/me", session!.AccessToken);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task ProfileChangesAndPasswordChange()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await RegisterCheckedAsync(instance, browser, "profile@example.org");
        await browser.SignInAsync("profile@example.org", Password);

        var patch = await browser.SendAsync(HttpMethod.Patch, "/api/v1/me", new { displayName = "  New name ", language = "fr" });
        var account = await patch.Content.ReadFromJsonAsync<AccountResponse>(TestContext.Current.CancellationToken);
        var wrongCurrent = await browser.PostAsync("/api/v1/me/password", new { currentPassword = "wrong password", newPassword = "the next password" });
        var change = await browser.PostAsync("/api/v1/me/password", new { currentPassword = Password, newPassword = "the next password" });

        Assert.Equal("New name", account!.DisplayName);
        Assert.Equal("fr", account.Language);
        Assert.Equal(AccountErrorCodes.InvalidCredentials, await wrongCurrent.CodeAsync());
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        await browser.SignInAsync("profile@example.org", "the next password");
    }

    [Fact]
    public async Task AuthenticationEndpointsHaveARateLimit()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        instance.Settings["Auth:RequestsPerMinute"] = "2";
        using var browser = instance.StartReplica().CreateBrowser();

        await browser.PostAsync("/api/v1/auth/sign-in", new { email = "a@example.org", password = Password });
        await browser.PostAsync("/api/v1/auth/sign-in", new { email = "a@example.org", password = Password });
        var third = await browser.PostAsync("/api/v1/auth/sign-in", new { email = "a@example.org", password = Password });

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

    internal static async Task RegisterCheckedAsync(AccountsInstance instance, BrowserClient browser, string email)
    {
        var register = await browser.PostAsync("/api/v1/auth/register", new { email, password = Password, displayName = email.Split('@')[0], language = "en" });
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        var check = await browser.PostAsync("/api/v1/auth/email-check", new { token = instance.Emails.LastToken(email) });
        Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
