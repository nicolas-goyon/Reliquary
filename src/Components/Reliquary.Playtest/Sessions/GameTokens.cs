using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The claims of a game token that the API read and checked.
/// </summary>
/// <param name="GameId">The game.</param>
/// <param name="Seat">The seat in the game.</param>
/// <param name="TokenId">The ID of the token (<c>jti</c>).</param>
internal sealed record GameTokenClaims(Guid GameId, int Seat, string TokenId);

/// <summary>
/// Creates and reads the game tokens: JSON Web Tokens signed with HMAC SHA-256 and the key of the access tokens.
/// </summary>
/// <remarks>
/// A game token gives access to one seat of one game. Its audience is <see cref="Audience"/>, so the API never accepts it as an access token,
/// and the game endpoints never accept an access token as a game token.
/// The seat keeps the ID of its only valid token, so a new join or a kick makes the old token not valid. The caller checks this ID.
/// </remarks>
internal sealed class GameTokens(IConfiguration configuration, TimeProvider time)
{
    /// <summary>The audience of the game tokens.</summary>
    public const string Audience = "reliquary-game";

    /// <summary>The claim of the game ID.</summary>
    public const string GameClaim = "game";

    /// <summary>The claim of the seat number.</summary>
    public const string SeatClaim = "seat";

    private readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false, MapInboundClaims = false };

    private string Issuer => configuration["Auth:Issuer"] is { Length: > 0 } issuer ? issuer : "reliquary";

    /// <summary>
    /// Creates a game token.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="seat">The seat.</param>
    /// <param name="expiresAt">The time when the token stops being valid.</param>
    /// <returns>The token and its ID.</returns>
    public (string Token, string TokenId) Create(Guid gameId, int seat, DateTimeOffset expiresAt)
    {
        var now = time.GetUtcNow();
        var tokenId = Guid.NewGuid().ToString("N");
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Jti] = tokenId,
                [GameClaim] = gameId.ToString(),
                [SeatClaim] = seat,
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256),
        });
        return (token, tokenId);
    }

    /// <summary>
    /// Reads a game token and checks its signature, its audience, its issuer and its lifetime.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The claims, or <see langword="null"/> when the token is not valid.</returns>
    public async Task<GameTokenClaims?> ReadAsync(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2048)
        {
            return null;
        }

        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = SigningKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return expires is { } end && now < end.AddSeconds(30) && (notBefore is not { } start || now >= start.AddSeconds(-30));
            },
        });
        if (!result.IsValid
            || !result.Claims.TryGetValue(GameClaim, out var game) || !Guid.TryParse(game?.ToString(), out var gameId)
            || !result.Claims.TryGetValue(SeatClaim, out var seat) || !int.TryParse(Convert.ToString(seat, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out var seatNumber)
            || !result.Claims.TryGetValue(JwtRegisteredClaimNames.Jti, out var jti) || jti?.ToString() is not { Length: > 0 } tokenId)
        {
            return null;
        }

        return new GameTokenClaims(gameId, seatNumber, tokenId);
    }

    private SymmetricSecurityKey SigningKey()
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuration["Auth:SigningKey"] ?? string.Empty);
        }
        catch (FormatException)
        {
            key = [];
        }

        return key.Length >= 32
            ? new SymmetricSecurityKey(key)
            : throw new InvalidOperationException("The setting Auth:SigningKey must contain 32 random bytes or more, in base64. The game tokens use the key of the access tokens.");
    }
}
