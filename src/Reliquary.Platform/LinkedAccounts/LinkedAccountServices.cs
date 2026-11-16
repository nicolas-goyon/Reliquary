using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Reliquary.Platform.LinkedAccounts;

/// <summary>
/// Adds the services of the linked accounts: the vault and the store of the links.
/// </summary>
internal static class LinkedAccountServices
{
    /// <summary>
    /// Adds the vault and the store. The host does not start when a vault key is not valid.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddLinkedAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VaultOptions>()
            .Configure(options => configuration.GetSection(VaultOptions.Section).Bind(options.Keys))
            .Validate(options => options.Keys.All(key => Vault.CheckKey(key.Key, key.Value) is null), "A vault key of Plugins:VaultKeys is not valid. Each key needs an ID of 1 to 64 letters, digits, dots, hyphens and underscores, and 32 bytes in Base64.")
            .ValidateOnStart();
        services.TryAddSingleton<Vault>();
        services.TryAddSingleton<AccountLinkStore>();
        return services;
    }
}
