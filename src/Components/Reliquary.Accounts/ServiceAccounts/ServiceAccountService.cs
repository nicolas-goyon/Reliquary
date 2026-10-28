using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Accounts.ServiceAccounts;

/// <summary>A service account. The response never contains the secret.</summary>
/// <param name="Id">The ID of the service account.</param>
/// <param name="Name">The unique name, for example <c>moxfield-sync</c>. The users see it when they give a delegation.</param>
/// <param name="Description">The description that the users see.</param>
/// <param name="Prefix">The visible start of the secret, after <c>rls_</c>.</param>
/// <param name="CreatedAt">The time of the creation.</param>
/// <param name="LastUsedAt">The approximate time of the last request with the secret.</param>
/// <param name="RevokedAt">The time of the revocation, or <see langword="null"/> for an active service account.</param>
public sealed record ServiceAccountResponse(Guid Id, string Name, string Description, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);

/// <summary>A new service account, with its secret. The API shows the secret only one time.</summary>
/// <param name="ServiceAccount">The service account.</param>
/// <param name="Secret">The secret. Keep it secret. The module sends it in the header <c>Authorization: Bearer</c>.</param>
public sealed record CreatedServiceAccountResponse(ServiceAccountResponse ServiceAccount, string Secret);

/// <summary>The body of a new service account.</summary>
/// <param name="Name">The unique name: 3 to 50 lowercase letters, digits and hyphens, with a letter first.</param>
/// <param name="Description">The description that the users see, with 200 characters or fewer.</param>
public sealed record CreateServiceAccountRequest(string? Name, string? Description);

/// <summary>The result of the creation of a service account.</summary>
/// <param name="Created">The new service account, or <see langword="null"/> after an error.</param>
/// <param name="Errors">The errors of the fields.</param>
/// <param name="NameTaken">Whether another service account has the same name.</param>
public sealed record ServiceAccountCreation(CreatedServiceAccountResponse? Created, IReadOnlyList<FieldError> Errors, bool NameTaken);

/// <summary>
/// Creates, lists and revokes the service accounts. The administrator uses it through the admin API and the CLI.
/// See docs/architecture/service-accounts.md.
/// </summary>
public sealed partial class ServiceAccountService
{
    /// <summary>The maximum length of a description.</summary>
    public const int MaxDescriptionLength = 200;

    private readonly ServiceAccountStore store;
    private readonly TimeProvider time;
    private readonly ILogger<ServiceAccountService> logger;

    internal ServiceAccountService(ServiceAccountStore store, TimeProvider time, ILogger<ServiceAccountService> logger)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
    }

    /// <summary>
    /// Creates a service account with a new secret.
    /// </summary>
    /// <param name="name">The unique name.</param>
    /// <param name="description">The description.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The result.</returns>
    public async Task<ServiceAccountCreation> CreateAsync(string? name, string? description, CancellationToken cancellationToken = default)
    {
        name = name?.Trim();
        description = description?.Trim() ?? string.Empty;
        var errors = new List<FieldError>();
        if (string.IsNullOrEmpty(name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.Required, "The name of the service account is necessary."));
        }
        else if (!NamePattern().IsMatch(name))
        {
            errors.Add(new FieldError("name", FieldErrorCodes.InvalidFormat, "The name must have 3 to 50 lowercase letters, digits and hyphens, with a letter first. Example: moxfield-sync."));
        }

        if (description.Length > MaxDescriptionLength)
        {
            errors.Add(new FieldError("description", FieldErrorCodes.TooLong, $"The description must have {MaxDescriptionLength} characters or fewer."));
        }

        if (errors.Count > 0)
        {
            return new ServiceAccountCreation(null, errors, false);
        }

        var (secret, prefix, secretHash) = PrefixedSecretFormat.ServiceAccount.Create();
        var row = new ServiceAccountRow
        {
            Id = Ids.New(),
            Name = name!,
            Description = description,
            Prefix = prefix,
            SecretHash = secretHash,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        if (!await store.InsertServiceAccountAsync(row, cancellationToken))
        {
            return new ServiceAccountCreation(null, [], true);
        }

        logger.ServiceAccountCreated(row.Id, row.Name);
        return new ServiceAccountCreation(new CreatedServiceAccountResponse(ToResponse(row), secret), [], false);
    }

    /// <summary>
    /// Lists the service accounts, sorted by name.
    /// </summary>
    /// <param name="includeRevoked">Whether the list contains the revoked service accounts.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The service accounts.</returns>
    public async Task<IReadOnlyList<ServiceAccountResponse>> ListAsync(bool includeRevoked, CancellationToken cancellationToken = default) =>
        (await store.ListServiceAccountsAsync(includeRevoked, cancellationToken)).Select(ToResponse).ToList();

    /// <summary>
    /// Finds a service account by its ID or by its name.
    /// </summary>
    /// <param name="idOrName">The ID or the name.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The service account, or <see langword="null"/>.</returns>
    public async Task<ServiceAccountResponse?> FindAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idOrName);
        var row = Guid.TryParse(idOrName, out var id)
            ? await store.FindServiceAccountAsync(id, cancellationToken)
            : await store.FindServiceAccountByNameAsync(idOrName.Trim(), cancellationToken);
        return row is null ? null : ToResponse(row);
    }

    /// <summary>
    /// Revokes a service account and all its delegations. The secret and the delegation tokens stop working immediately.
    /// </summary>
    /// <param name="id">The ID of the service account.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns><see langword="true"/> when the service account was active.</returns>
    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var revoked = await store.RevokeServiceAccountAsync(id, time.GetUtcNow(), cancellationToken);
        if (revoked)
        {
            logger.ServiceAccountRevoked(id);
        }

        return revoked;
    }

    internal static ServiceAccountResponse ToResponse(ServiceAccountRow row) => new(
        row.Id,
        row.Name,
        row.Description,
        row.Prefix,
        Utc(row.CreatedAt),
        row.LastUsedAt is { } lastUsedAt ? Utc(lastUsedAt) : null,
        row.RevokedAt is { } revokedAt ? Utc(revokedAt) : null);

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    [GeneratedRegex("^[a-z][a-z0-9-]{2,49}$")]
    private static partial Regex NamePattern();
}
