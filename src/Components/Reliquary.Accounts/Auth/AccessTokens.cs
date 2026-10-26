using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Reliquary.Accounts.Data;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.Auth;

/// <summary>
/// Creates the access tokens of the web sessions: JSON Web Tokens signed with HMAC SHA-256.
/// </summary>
internal sealed class AccessTokens(IOptions<AuthOptions> options, TimeProvider time)
{
    private readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };

    /// <summary>
    /// Creates an access token for an account.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>The token and the time when it stops being valid.</returns>
    public (string Token, DateTimeOffset ExpiresAt) Create(AccountRow account)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var expiresAt = now + settings.AccessTokenLifetime;
        var claims = new Dictionary<string, object>
        {
            [ReliquaryClaims.AccountId] = account.Id.ToString(),
            [ReliquaryClaims.DisplayName] = account.DisplayName,
            [ReliquaryClaims.Language] = account.Language,
            [ReliquaryClaims.AuthMethod] = AuthMethods.Session,
        };
        if (account.IsAdministrator)
        {
            claims[ReliquaryClaims.Role] = Roles.Administrator;
        }

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(settings), SecurityAlgorithms.HmacSha256),
        });
        return (token, expiresAt);
    }

    /// <summary>
    /// Gets the parameters that validate an access token.
    /// </summary>
    /// <param name="settings">The settings of the authentication.</param>
    /// <returns>The parameters.</returns>
    public static TokenValidationParameters ValidationParameters(AuthOptions settings) => new()
    {
        ValidIssuer = settings.Issuer,
        ValidAudience = settings.Audience,
        IssuerSigningKey = SigningKey(settings),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ReliquaryClaims.DisplayName,
        RoleClaimType = ReliquaryClaims.Role,
    };

    private static SymmetricSecurityKey SigningKey(AuthOptions settings)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(settings.SigningKey);
        }
        catch (FormatException)
        {
            key = [];
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "The setting Auth:SigningKey must contain 32 random bytes or more, in base64. " +
                $"Set the same value on all the API replicas. For example: {Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))}");
        }

        return new SymmetricSecurityKey(key);
    }
}

/// <summary>
/// Reads the claims of a principal for the Accounts endpoints.
/// </summary>
internal static class PrincipalClaims
{
    /// <summary>
    /// Makes a principal for an API key.
    /// </summary>
    /// <param name="key">The API key and its account.</param>
    /// <param name="scheme">The authentication scheme.</param>
    /// <returns>The principal.</returns>
    public static ClaimsPrincipal ForApiKey(ApiKeyRow key, string scheme)
    {
        var claims = new List<Claim>
        {
            new(ReliquaryClaims.AccountId, key.AccountId.ToString()),
            new(ReliquaryClaims.DisplayName, key.DisplayName),
            new(ReliquaryClaims.Language, key.Language),
            new(ReliquaryClaims.AuthMethod, AuthMethods.ApiKey),
            new(ReliquaryClaims.ApiKeyId, key.Id.ToString()),
        };
        if (key.IsAdministrator)
        {
            claims.Add(new Claim(ReliquaryClaims.Role, Roles.Administrator));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, ReliquaryClaims.DisplayName, ReliquaryClaims.Role));
    }
}
