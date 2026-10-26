using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks.Demo;

namespace Reliquary.Accounts.Tests;

public sealed class DemoAccountsTests
{
    [Fact]
    public async Task DemoAccountsCanSignInAndASecondLoadChangesNothing()
    {
        await using var instance = await AccountsInstance.CreateAsync();
        var replica = instance.StartReplica();
        var seeder = replica.Services.GetServices<IDemoDataSeeder>().Single(seeder => seeder.Name == "accounts");

        var first = await seeder.SeedAsync(TestContext.Current.CancellationToken);
        var second = await seeder.SeedAsync(TestContext.Current.CancellationToken);
        using var browser = replica.CreateBrowser();
        var session = await browser.SignInAsync("demo@reliquary.localhost", "reliquary-demo");

        Assert.StartsWith("3 new demo accounts", first, StringComparison.Ordinal);
        Assert.StartsWith("0 new demo accounts", second, StringComparison.Ordinal);
        Assert.Equal("Demo", session.Account.DisplayName);
        Assert.Empty(instance.Emails.Messages);
    }
}
