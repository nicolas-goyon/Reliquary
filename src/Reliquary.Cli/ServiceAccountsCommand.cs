using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Accounts.ServiceAccounts;

namespace Reliquary.Cli;

/// <summary>
/// The <c>service-accounts</c> commands: they create, list and revoke the service accounts of the modules.
/// See docs/architecture/service-accounts.md.
/// </summary>
internal static class ServiceAccountsCommand
{
    public static Command Create()
    {
        var name = new Argument<string>("name") { Description = "The unique name: 3 to 50 lowercase letters, digits and hyphens, with a letter first. Example: moxfield-sync." };
        var description = new Option<string>("--description") { Description = "The description that the users see when they give a delegation." };
        var create = new Command("create", "Create a service account. The command shows the secret one time only.") { name, description };
        create.SetAction(async (result, cancellationToken) =>
        {
            using var host = CliHost.Build();
            var service = host.Services.GetRequiredService<ServiceAccountService>();
            var creation = await service.CreateAsync(result.GetValue(name), result.GetValue(description), cancellationToken);
            foreach (var error in creation.Errors)
            {
                await Console.Error.WriteLineAsync(error.Message);
            }

            if (creation.NameTaken)
            {
                await Console.Error.WriteLineAsync("A service account with this name exists. Use a different name.");
            }

            if (creation.Created is not { } created)
            {
                return 2;
            }

            Console.WriteLine($"The service account {created.ServiceAccount.Name} is ready. ID: {created.ServiceAccount.Id}");
            Console.WriteLine("Copy the secret now. The CLI and the API show it one time only.");
            Console.WriteLine(created.Secret);
            return 0;
        });

        var all = new Option<bool>("--all") { Description = "Also list the revoked service accounts." };
        var list = new Command("list", "List the service accounts.") { all };
        list.SetAction(async (result, cancellationToken) =>
        {
            using var host = CliHost.Build();
            var service = host.Services.GetRequiredService<ServiceAccountService>();
            var accounts = await service.ListAsync(result.GetValue(all), cancellationToken);
            if (accounts.Count == 0)
            {
                Console.WriteLine("The instance has no service account.");
            }

            foreach (var account in accounts)
            {
                var state = account.RevokedAt is { } revokedAt ? $"revoked {Format(revokedAt)}" : "active";
                var lastUse = account.LastUsedAt is { } lastUsedAt ? Format(lastUsedAt) : "never";
                Console.WriteLine($"{account.Id}  {account.Name}  rls_{account.Prefix}_…  {state}  last use: {lastUse}  {account.Description}");
            }

            return 0;
        });

        var target = new Argument<string>("service-account") { Description = "The ID or the name of the service account." };
        var revoke = new Command("revoke", "Revoke a service account and all its delegations. The secret and the delegation tokens stop working immediately.") { target };
        revoke.SetAction(async (result, cancellationToken) =>
        {
            using var host = CliHost.Build();
            var service = host.Services.GetRequiredService<ServiceAccountService>();
            var account = await service.FindAsync(result.GetValue(target)!, cancellationToken);
            if (account is null || account.RevokedAt is not null)
            {
                await Console.Error.WriteLineAsync($"No active service account has the ID or the name \"{result.GetValue(target)}\". Run \"reliquary service-accounts list\" to see the service accounts.");
                return 2;
            }

            await service.RevokeAsync(account.Id, cancellationToken);
            Console.WriteLine($"The service account {account.Name} and its delegations are revoked.");
            return 0;
        });

        return new Command("service-accounts", "Create, list and revoke the service accounts of the modules.") { create, list, revoke };
    }

    private static string Format(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
