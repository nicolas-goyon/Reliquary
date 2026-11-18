using Dapper;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Platform.LinkedAccounts;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// The plugin data of an owner for the export and the deletion of the personal data: the plugin activations with the consent, and the linked accounts.
/// See docs/decisions/0032-personal-data.md and docs/decisions/0051-linked-accounts.md.
/// </summary>
/// <param name="database">The databases of the instance.</param>
/// <param name="activations">The plugin activations of the users.</param>
/// <param name="links">The linked accounts of the users.</param>
internal sealed class PluginPersonalData(IDatabase database, PluginActivations activations, AccountLinkStore links) : IPersonalDataProvider
{
    /// <inheritdoc />
    public string Name => "plugins";

    /// <inheritdoc />
    /// <remarks>The archive never contains the secret of a linked account. It tells only whether the link has a secret.</remarks>
    public async Task ExportAsync(Guid ownerId, IPersonalDataArchive archive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var plugins = (await activations.ListAsync(ownerId, cancellationToken)).Values
            .OrderBy(activation => activation.ExtensionId, StringComparer.Ordinal)
            .Select(activation => new
            {
                PluginId = activation.ExtensionId,
                activation.Enabled,
                activation.AcceptedVersion,
                AcceptedScopes = activation.Accepted.Scopes,
                AcceptedNetwork = activation.Accepted.Network,
                AcceptedDevices = activation.Accepted.Devices,
                AcceptedSites = activation.Accepted.Sites,
                activation.AcceptedAt,
            })
            .ToList();
        await archive.WriteJsonAsync("plugins.json", plugins, cancellationToken);

        var accounts = (await links.ListAsync(ownerId, cancellationToken))
            .Select(link => new
            {
                PluginId = link.ExtensionId,
                link.Site,
                link.AuthMethod,
                UserName = link.DisplayName,
                HasSecret = link.KeyId is not null,
                link.State,
                LinkedAt = Utc(link.LinkedAt),
                LastSyncAt = link.LastSyncAt is { } at ? Utc(at) : (DateTimeOffset?)null,
                link.LastSyncError,
            })
            .ToList();
        await archive.WriteJsonAsync("linked-accounts.json", accounts, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenContentAsync(ownerId, ContentAccess.Write, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            delete from content.account_links where owner_id = @ownerId;
            delete from content.plugin_activations where owner_id = @ownerId;
            """,
            new { ownerId },
            cancellationToken: cancellationToken));
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
