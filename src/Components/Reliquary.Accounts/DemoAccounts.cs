using Reliquary.Accounts.Auth;
using Reliquary.BuildingBlocks.Demo;

namespace Reliquary.Accounts;

/// <summary>
/// Creates the demo accounts of a local instance. Their email addresses are checked, so they can sign in at once.
/// </summary>
/// <remarks>
/// The passwords are public. Do not load the demo data on an instance that other people can reach.
/// </remarks>
internal sealed class DemoAccounts(AccountService accounts) : IDemoDataSeeder
{
    /// <summary>The demo accounts: email address, password, display name and language.</summary>
    public static readonly IReadOnlyList<(string Email, string Password, string DisplayName, string Language)> All =
    [
        ("demo@reliquary.localhost", "reliquary-demo", "Demo", "en"),
        ("demo-fr@reliquary.localhost", "reliquary-demo", "Démo", "fr"),
        ("admin@reliquary.localhost", "reliquary-admin", "Admin", "en"),
    ];

    /// <inheritdoc />
    public string Name => "accounts";

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public async Task<string> SeedAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var (email, password, displayName, language) in All)
        {
            if (await accounts.CreateCheckedAccountAsync(email, password, displayName, language, cancellationToken))
            {
                created++;
            }
        }

        return $"{created} new demo accounts, {All.Count - created} accounts already existed.";
    }
}
