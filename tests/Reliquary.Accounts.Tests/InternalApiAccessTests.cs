using System.Net;

namespace Reliquary.Accounts.Tests;

/// <summary>
/// Checks that the credentials of a user cannot use the internal API. See docs/architecture/internal-api.md.
/// </summary>
public sealed class InternalApiAccessTests
{
    [Fact]
    public async Task InternalApiRefusesTheCredentialsOfAUser()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        using var browser = instance.StartReplica().CreateBrowser();
        await AuthFlowTests.RegisterCheckedAsync(instance, browser, "internal@example.org");

        var anonymous = await browser.GetAsync("/internal/v1");
        await browser.SignInAsync("internal@example.org", AuthFlowTests.Password);
        var withSession = await browser.GetAsync("/internal/v1/events/types");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withSession.StatusCode);
    }
}
