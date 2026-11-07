using Microsoft.Extensions.DependencyInjection;
using Reliquary.Accounts.Auth;
using Reliquary.Accounts.Data;

namespace Reliquary.Accounts.Tests;

public sealed class ExternalSignInTests
{
    [Fact]
    public async Task NewProviderAccountCreatesACheckedAccount()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var accounts = instance.StartReplica().Services.GetRequiredService<AccountService>();

        var (session, error) = await accounts.ExternalSignInAsync(
            new ExternalIdentity("github", "1001", "Gh@Example.org", true, "Octo", "fr"), TestContext.Current.CancellationToken);
        var (again, _) = await accounts.ExternalSignInAsync(
            new ExternalIdentity("github", "1001", "other@example.org", true, "Octo", "en"), TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(session!.RefreshToken);
        Assert.Equal("Octo", session.Account.DisplayName);
        Assert.Equal("fr", session.Account.Language);
        Assert.NotNull(session.Account.EmailCheckedAt);
        Assert.Null(session.Account.PasswordHash);
        Assert.Equal(session.Account.Id, again!.Account.Id);
    }

    [Fact]
    public async Task VerifiedAddressLinksTheProviderToTheExistingAccount()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var replica = instance.StartReplica();
        using var browser = replica.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, browser, "link@example.org");
        var accounts = replica.Services.GetRequiredService<AccountService>();
        var store = replica.Services.GetRequiredService<AccountStore>();

        var (session, _) = await accounts.ExternalSignInAsync(
            new ExternalIdentity("google", "g-1", "LINK@example.org", true, "Link", "en"), TestContext.Current.CancellationToken);
        var providers = await store.ListExternalProvidersAsync(session!.Account.Id, TestContext.Current.CancellationToken);

        Assert.Equal("link", session.Account.DisplayName);
        Assert.Equal(["google"], providers);
    }

    [Fact]
    public async Task AddressThatTheProviderDidNotCheckIsNotLinked()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var replica = instance.StartReplica();
        using var browser = replica.CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, browser, "victim@example.org");
        var accounts = replica.Services.GetRequiredService<AccountService>();

        var (session, error) = await accounts.ExternalSignInAsync(
            new ExternalIdentity("discord", "d-1", "victim@example.org", false, "Attacker", "en"), TestContext.Current.CancellationToken);
        var (noEmail, noEmailError) = await accounts.ExternalSignInAsync(
            new ExternalIdentity("discord", "d-2", null, false, "Nobody", "en"), TestContext.Current.CancellationToken);

        Assert.Null(session);
        Assert.Equal(AccountErrorCodes.ExternalEmailNotVerified, error);
        Assert.Null(noEmail);
        Assert.Equal(AccountErrorCodes.ExternalEmailMissing, noEmailError);
    }

    [Fact]
    public async Task ProvidersListContainsOnlyTheConfiguredProviders()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        instance.Settings["Auth:Providers:GitHub:ClientId"] = "id";
        instance.Settings["Auth:Providers:GitHub:ClientSecret"] = "secret";
        instance.Settings["Auth:Providers:Google:ClientId"] = "id-without-secret";
        using var browser = instance.StartReplica().CreateBrowser();

        var providers = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<ProvidersResponse>(
            browser.Http, new Uri("/api/v1/auth/providers", UriKind.Relative), TestContext.Current.CancellationToken);
        var challenge = await browser.GetAsync("/api/v1/auth/external/github?returnUrl=/decks");
        var disabled = await browser.GetAsync("/api/v1/auth/external/google");

        Assert.Equal(["github"], providers!.Providers);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.StartsWith("https://github.com/login/oauth/authorize", challenge.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, disabled.StatusCode);
        Assert.Equal(AccountErrorCodes.ProviderNotEnabled, await disabled.CodeAsync());
    }
}
