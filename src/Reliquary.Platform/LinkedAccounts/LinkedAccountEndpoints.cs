using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Platform.Extensions;

namespace Reliquary.Platform.LinkedAccounts;

/// <summary>An external site of a plugin, with the linked account of the user on this site, or no linked account.</summary>
/// <param name="PluginId">The ID of the plugin.</param>
/// <param name="PluginName">The name of the plugin in English, or null when the instance does not have the plugin now.</param>
/// <param name="PluginActive">Whether the plugin runs for the user. A link of a plugin that does not run stays until the user unlinks it.</param>
/// <param name="Site">The host name of the site, for example <c>moxfield.com</c>.</param>
/// <param name="Auth">The authentication method: <c>public-username</c>, <c>api-key</c> or <c>oauth2-pkce</c>.</param>
/// <param name="Sync">The sync of the plugin manifest, or null when the plugin does not run.</param>
/// <param name="Link">The linked account, or null when the user did not link an account on this site.</param>
public sealed record LinkedAccountResponse(
    string PluginId,
    string? PluginName,
    bool PluginActive,
    string Site,
    string Auth,
    LinkedAccountSyncResponse? Sync,
    LinkedAccountLinkResponse? Link);

/// <summary>When the core starts a sync of the linked account.</summary>
/// <param name="Mode"><c>manual</c>, <c>on-open</c> or <c>periodic</c>.</param>
/// <param name="IntervalMinutes">The interval of a periodic sync, in minutes: 15 at least.</param>
public sealed record LinkedAccountSyncResponse(string Mode, int? IntervalMinutes);

/// <summary>A linked account, without its secret.</summary>
/// <param name="UserName">The name of the account on the site, or null.</param>
/// <param name="HasSecret">Whether the vault keeps a secret for the link, for example an API key.</param>
/// <param name="SecretReadable">False when the instance lost the vault key of the secret. The user must link the account again.</param>
/// <param name="State"><c>linked</c>, <c>synced</c> or <c>failed</c>.</param>
/// <param name="LinkedAt">The time of the link.</param>
/// <param name="LastSyncAt">The time of the last sync, or null.</param>
/// <param name="LastSyncError">The message of the plugin after the last failed sync, or null.</param>
public sealed record LinkedAccountLinkResponse(
    string? UserName,
    bool HasSecret,
    bool SecretReadable,
    string State,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastSyncAt,
    string? LastSyncError);

/// <summary>The linked accounts of the user.</summary>
/// <param name="Items">The sites of the plugins that run for the user, then the links of the plugins that do not run.</param>
public sealed record LinkedAccountListResponse(IReadOnlyList<LinkedAccountResponse> Items);

/// <summary>The body of a link.</summary>
/// <param name="UserName">The public user name. Necessary for <c>public-username</c>, optional for <c>api-key</c>.</param>
/// <param name="Secret">The API key. Necessary for <c>api-key</c>. The API never sends it to the web app again.</param>
public sealed record LinkAccountRequest(string? UserName, string? Secret);

/// <summary>The result of a sync that the web app ran.</summary>
/// <param name="Succeeded">Whether the sync completed.</param>
/// <param name="Error">The message of the plugin after a failed sync. The API keeps 200 characters at most.</param>
public sealed record LinkedAccountSyncRequest(bool? Succeeded, string? Error);

/// <summary>The linked account of a site, with its secret, for the plugin of the link.</summary>
/// <param name="Site">The host name of the site.</param>
/// <param name="Auth">The authentication method.</param>
/// <param name="UserName">The name of the account on the site, or null.</param>
/// <param name="Secret">The secret, or null for a link with a public user name.</param>
public sealed record LinkedAccountSecretResponse(string Site, string Auth, string? UserName, string? Secret);

/// <summary>
/// The endpoints of the linked accounts. See docs/decisions/0051-linked-accounts.md.
/// </summary>
public static class LinkedAccountEndpoints
{
    /// <summary>The instance has no vault key, so it cannot keep a secret.</summary>
    public const string VaultNotConfigured = "vault_not_configured";

    /// <summary>The instance does not have the vault key of the secret any more.</summary>
    public const string VaultKeyMissing = "vault_key_missing";

    /// <summary>The vault cannot decrypt the secret.</summary>
    public const string SecretUnreadable = "vault_secret_unreadable";

    /// <summary>The core does not support the authentication method of the site yet.</summary>
    public const string MethodNotSupported = "linked_account_method_not_supported";

    /// <summary>The maximum length of a user name.</summary>
    public const int MaxUserNameLength = 100;

    /// <summary>The maximum length of a secret.</summary>
    public const int MaxSecretLength = 4096;

    /// <summary>The maximum length of the message of a failed sync.</summary>
    public const int MaxSyncErrorLength = 200;

    /// <summary>
    /// Adds the endpoints of the linked accounts to the public API.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    public static void MapLinkedAccountEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me/linked-accounts", ListAsync)
            .WithTags("Linked accounts")
            .WithName("ListLinkedAccounts")
            .WithSummary("Lists the external sites of the plugins that run for the user, with the linked account of each site. The response never contains a secret.")
            .RequireAuthorization(AuthPolicies.Session);

        api.MapPut("/me/extensions/{id}/accounts/{site}", LinkAsync)
            .WithTags("Linked accounts")
            .WithName("LinkAccount")
            .WithSummary("Links an account on an external site of a plugin, or replaces the link. The vault encrypts the API key.")
            .RequireAuthorization(AuthPolicies.Session);

        api.MapDelete("/me/extensions/{id}/accounts/{site}", UnlinkAsync)
            .WithTags("Linked accounts")
            .WithName("UnlinkAccount")
            .WithSummary("Unlinks an account. The API deletes the link and its secret.")
            .RequireAuthorization(AuthPolicies.Session);

        api.MapPost("/me/extensions/{id}/accounts/{site}/sync", RecordSyncAsync)
            .WithTags("Linked accounts")
            .WithName("RecordLinkedAccountSync")
            .WithSummary("Records the result of a sync of a linked account. The web app runs the sync with the plugin, then calls this endpoint.")
            .RequireAuthorization(AuthPolicies.Session);

        api.MapGet("/me/extensions/{id}/accounts/{site}/secret", GetSecretAsync)
            .WithTags("Linked accounts")
            .WithName("GetLinkedAccountSecret")
            .WithSummary("Gives the linked account of a site with its secret. Only a plugin token of the plugin {id} can call it.")
            .RequirePluginToken();
    }

    private static async Task<Ok<LinkedAccountListResponse>> ListAsync(
        ClaimsPrincipal user, ExtensionRegistry registry, IPluginAccess access, AccountLinkStore store, Vault vault, CancellationToken cancellationToken)
    {
        var ownerId = user.GetAccountId();
        var links = (await store.ListAsync(ownerId, cancellationToken)).ToDictionary(row => (row.ExtensionId, row.Site));
        var items = new List<LinkedAccountResponse>();
        var plugins = await registry.ListAsync(enabledOnly: true, cancellationToken);
        var names = plugins.ToDictionary(row => row.Id, row => row.Name, StringComparer.Ordinal);
        foreach (var plugin in plugins.Where(row => row.Kind == "plugin"))
        {
            var sites = ManifestAccounts.Read(plugin.Manifest);
            if (sites.Count == 0 || await access.ScopesAsync(ownerId, plugin.Id, cancellationToken) is null)
            {
                continue;
            }

            foreach (var site in sites)
            {
                var link = links.Remove((plugin.Id, site.Site), out var row) ? ToLink(row, vault) : null;
                items.Add(new LinkedAccountResponse(
                    plugin.Id, plugin.Name, true, site.Site, site.Auth, new LinkedAccountSyncResponse(site.SyncMode, site.IntervalMinutes), link));
            }
        }

        // The links of the plugins that do not run, so the user can unlink them.
        items.AddRange(links.Values.Select(row => new LinkedAccountResponse(
            row.ExtensionId, names.GetValueOrDefault(row.ExtensionId), false, row.Site, row.AuthMethod, null, ToLink(row, vault))));
        return TypedResults.Ok(new LinkedAccountListResponse(items));
    }

    private static async Task<Results<Ok<LinkedAccountResponse>, ProblemHttpResult>> LinkAsync(
        string id,
        string site,
        LinkAccountRequest request,
        ClaimsPrincipal user,
        ExtensionRegistry registry,
        IPluginAccess access,
        AccountLinkStore store,
        Vault vault,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var ownerId = user.GetAccountId();
        if (await FindSiteAsync(ownerId, id, site, registry, access, cancellationToken) is not { } found)
        {
            return ApiProblems.NotFound("No plugin that runs for you declares this site. Activate the plugin, then try again.");
        }

        var (plugin, account) = found;

        if (account.Auth is not (ManifestAccounts.PublicUserName or ManifestAccounts.ApiKey))
        {
            return ApiProblems.Conflict(MethodNotSupported, $"The core does not support the authentication method \"{account.Auth}\" yet.");
        }

        var userName = request.UserName?.Trim() is { Length: > 0 } name ? name : null;
        var secret = request.Secret?.Trim() is { Length: > 0 } value ? value : null;
        var errors = new List<FieldError>();
        if (userName is null && account.Auth == ManifestAccounts.PublicUserName)
        {
            errors.Add(new FieldError("userName", FieldErrorCodes.Required, "The site needs your public user name."));
        }
        else if (userName is { Length: > MaxUserNameLength })
        {
            errors.Add(new FieldError("userName", FieldErrorCodes.TooLong, $"The user name must have {MaxUserNameLength} characters or fewer."));
        }
        else if (userName is not null && userName.Any(char.IsControl))
        {
            errors.Add(new FieldError("userName", FieldErrorCodes.InvalidFormat, "The user name must not contain control characters."));
        }

        if (account.Auth == ManifestAccounts.PublicUserName && request.Secret is not null)
        {
            errors.Add(new FieldError("secret", FieldErrorCodes.NotAllowed, "The site uses a public user name. Do not send a secret."));
        }
        else if (account.Auth == ManifestAccounts.ApiKey && secret is null)
        {
            errors.Add(new FieldError("secret", FieldErrorCodes.Required, "The site needs your API key."));
        }
        else if (secret is { Length: > MaxSecretLength })
        {
            errors.Add(new FieldError("secret", FieldErrorCodes.TooLong, $"The API key must have {MaxSecretLength} characters or fewer."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        SealedSecret? sealedSecret = null;
        if (secret is not null)
        {
            if (!vault.IsConfigured)
            {
                return ApiProblems.Problem(
                    StatusCodes.Status503ServiceUnavailable,
                    VaultNotConfigured,
                    "The instance cannot keep an API key, because the administrator did not set a vault key. Tell the administrator.");
            }

            sealedSecret = vault.Encrypt(secret, new SecretScope(ownerId, id, account.Site));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var row = new AccountLinkRow
        {
            OwnerId = ownerId,
            ExtensionId = id,
            Site = account.Site,
            AuthMethod = account.Auth,
            DisplayName = userName,
            SecretCiphertext = sealedSecret?.Ciphertext,
            KeyId = sealedSecret?.KeyId,
            State = AccountLinkStates.Linked,
            LinkedAt = now,
            UpdatedAt = now,
        };
        await store.SaveAsync(row, cancellationToken);
        return TypedResults.Ok(new LinkedAccountResponse(
            id, plugin.Name, true, account.Site, account.Auth, new LinkedAccountSyncResponse(account.SyncMode, account.IntervalMinutes), ToLink(row, vault)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnlinkAsync(
        string id, string site, ClaimsPrincipal user, AccountLinkStore store, CancellationToken cancellationToken)
    {
        return await store.DeleteAsync(user.GetAccountId(), id, site, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.NotFound("You have no linked account for this plugin and this site.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RecordSyncAsync(
        string id, string site, LinkedAccountSyncRequest request, ClaimsPrincipal user, AccountLinkStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        if (request.Succeeded is not { } succeeded)
        {
            return ApiProblems.Validation(new FieldError("succeeded", FieldErrorCodes.Required, "The field \"succeeded\" is necessary."));
        }

        var error = succeeded ? null : Clean(request.Error);
        return await store.RecordSyncAsync(user.GetAccountId(), id, site, succeeded, error, time.GetUtcNow().UtcDateTime, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.NotFound("You have no linked account for this plugin and this site.");
    }

    /// <summary>Removes the control characters of the message of a plugin, and keeps <see cref="MaxSyncErrorLength"/> characters at most.</summary>
    private static string? Clean(string? message)
    {
        if (message is null)
        {
            return null;
        }

        var text = new string([.. message.Select(character => char.IsControl(character) ? ' ' : character)]).Trim();
        return text.Length == 0 ? null : text.Length > MaxSyncErrorLength ? text[..MaxSyncErrorLength] : text;
    }

    private static async Task<Results<Ok<LinkedAccountSecretResponse>, ProblemHttpResult>> GetSecretAsync(
        string id,
        string site,
        HttpContext http,
        ExtensionRegistry registry,
        IPluginAccess access,
        AccountLinkStore store,
        Vault vault,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (http.User.GetPluginId() != id)
        {
            return ApiProblems.Problem(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "The plugin token belongs to a different plugin. A plugin can read only its own secrets.");
        }

        var ownerId = http.User.GetAccountId();
        if (await FindSiteAsync(ownerId, id, site, registry, access, cancellationToken) is null
            || await store.FindAsync(ownerId, id, site, cancellationToken) is not { } row)
        {
            return ApiProblems.NotFound("The user has no linked account for this site.");
        }

        http.Response.Headers.CacheControl = "no-store";
        if (row.Secret is not { } sealedSecret)
        {
            return TypedResults.Ok(new LinkedAccountSecretResponse(row.Site, row.AuthMethod, row.DisplayName, null));
        }

        var scope = new SecretScope(ownerId, id, row.Site);
        var logger = loggers.CreateLogger(typeof(LinkedAccountEndpoints).FullName!);
        string secret;
        try
        {
            secret = vault.Decrypt(sealedSecret, scope);
        }
        catch (VaultKeyMissingException exception)
        {
            logger.VaultKeyMissing(exception.KeyId, id, row.Site);
            return ApiProblems.Conflict(VaultKeyMissing, "The instance lost the key of this secret. Ask the user to link the account again.");
        }
        catch (CryptographicException exception)
        {
            logger.SecretUnreadable(exception, id, row.Site);
            return ApiProblems.Conflict(SecretUnreadable, "The vault cannot decrypt this secret. Ask the user to link the account again.");
        }

        // A secret of an old key gets the current key, so the operator can delete the old key later.
        if (sealedSecret.KeyId != vault.CurrentKeyId)
        {
            await store.ReplaceSecretAsync(row, sealedSecret, vault.Encrypt(secret, scope), cancellationToken);
        }

        return TypedResults.Ok(new LinkedAccountSecretResponse(row.Site, row.AuthMethod, row.DisplayName, secret));
    }

    /// <summary>Finds a site in the manifest of a plugin that runs for the user.</summary>
    private static async Task<(ExtensionRow Plugin, ManifestAccount Account)?> FindSiteAsync(
        Guid ownerId, string pluginId, string site, ExtensionRegistry registry, IPluginAccess access, CancellationToken cancellationToken)
    {
        if (await access.ScopesAsync(ownerId, pluginId, cancellationToken) is null
            || await registry.FindAsync(pluginId, cancellationToken) is not { } plugin)
        {
            return null;
        }

        var account = ManifestAccounts.Read(plugin.Manifest).FirstOrDefault(item => string.Equals(item.Site, site, StringComparison.Ordinal));
        return account is null ? null : (plugin, account);
    }

    private static LinkedAccountLinkResponse ToLink(AccountLinkRow row, Vault vault) => new(
        row.DisplayName,
        row.KeyId is not null,
        row.KeyId is null || vault.HasKey(row.KeyId),
        row.State,
        new DateTimeOffset(DateTime.SpecifyKind(row.LinkedAt, DateTimeKind.Utc)),
        row.LastSyncAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
        row.LastSyncError);
}
