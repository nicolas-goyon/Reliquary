using Reliquary.Accounts.Auth;
using Reliquary.BuildingBlocks.Demo;

namespace Reliquary.Accounts;

/// <summary>
/// Creates the demo accounts of a local instance. A user opens them with the development sign-in.
/// </summary>
internal sealed class DemoAccounts(AccountService accounts) : IDemoDataSeeder
{
    /// <summary>The demo accounts: email address, display name and language.</summary>
    public static readonly IReadOnlyList<(string Email, string DisplayName, string Language)> All =
    [
        ("demo@reliquary.localhost", "Demo", "en"),
        ("demo-fr@reliquary.localhost", "Démo", "fr"),
        ("admin@reliquary.localhost", "Admin", "en"),
    ];

    /// <inheritdoc />
    public string Name => "accounts";

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public async Task<string> SeedAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var (email, displayName, language) in All)
        {
            if (await accounts.CreateAccountAsync(email, displayName, language, "demo", cancellationToken) is not null)
            {
                created++;
            }
        }

        return $"{created} new demo accounts, {All.Count - created} accounts already existed.";
    }
}
