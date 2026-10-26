using System.Security.Claims;

namespace Reliquary.BuildingBlocks.Security;

/// <summary>
/// The claims of an authenticated user. The Accounts component sets them, and the other core components read them.
/// </summary>
public static class ReliquaryClaims
{
    /// <summary>The ID of the account. The owner of the user data has the same ID.</summary>
    public const string AccountId = "sub";

    /// <summary>The display name of the account.</summary>
    public const string DisplayName = "name";

    /// <summary>The interface language of the account: <c>en</c> or <c>fr</c>.</summary>
    public const string Language = "lang";

    /// <summary>The authentication method. See <see cref="AuthMethods"/>.</summary>
    public const string AuthMethod = "amr";

    /// <summary>The role of the account. The only role is <see cref="Roles.Administrator"/>.</summary>
    public const string Role = "role";

    /// <summary>The ID of the API key, when the method is <see cref="AuthMethods.ApiKey"/>.</summary>
    public const string ApiKeyId = "rlq_key";
}

/// <summary>
/// The authentication methods.
/// </summary>
public static class AuthMethods
{
    /// <summary>An access token of a web session.</summary>
    public const string Session = "session";

    /// <summary>A personal API key.</summary>
    public const string ApiKey = "api_key";
}

/// <summary>
/// The roles of an account.
/// </summary>
public static class Roles
{
    /// <summary>The administrator of the instance.</summary>
    public const string Administrator = "administrator";
}

/// <summary>
/// The names of the authorization policies. Use them with <c>RequireAuthorization</c>.
/// </summary>
public static class AuthPolicies
{
    /// <summary>An authenticated user, with an access token or an API key.</summary>
    public const string User = "user";

    /// <summary>An authenticated user with the access token of a web session. An API key is not sufficient.</summary>
    public const string Session = "session";

    /// <summary>An administrator of the instance, with the access token of a web session.</summary>
    public const string Administrator = "administrator";
}

/// <summary>
/// Reads the claims of a user.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the ID of the account of an authenticated user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>The ID of the account.</returns>
    /// <exception cref="InvalidOperationException">The user is not authenticated.</exception>
    public static Guid GetAccountId(this ClaimsPrincipal user) =>
        user.FindAccountId() ?? throw new InvalidOperationException("The user is not authenticated. Use RequireAuthorization on the endpoint.");

    /// <summary>
    /// Gets the ID of the account of a user, when the user is authenticated.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>The ID of the account, or <see langword="null"/>.</returns>
    public static Guid? FindAccountId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirst(ReliquaryClaims.AccountId)?.Value, out var id) ? id : null;
    }
}
