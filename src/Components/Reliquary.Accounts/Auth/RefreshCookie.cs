using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Accounts.Auth;

/// <summary>
/// The cookie that contains the refresh token of a web session.
/// </summary>
/// <remarks>
/// The cookie is HttpOnly, so the scripts of the page cannot read it.
/// It is SameSite=Strict, so another site cannot use it. The browser sends it only to the authentication endpoints.
/// </remarks>
internal static class RefreshCookie
{
    /// <summary>The name of the cookie.</summary>
    public const string Name = "reliquary_refresh";

    /// <summary>The path of the cookie.</summary>
    public const string Path = ApiRoutes.Base + "/auth";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static void Write(HttpResponse response, Session session)
    {
        if (session.RefreshToken is null || session.RefreshTokenExpiresAt is not { } expiresAt)
        {
            return;
        }

        response.Cookies.Append(Name, session.RefreshToken, Options(expiresAt));
    }

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true,
    };
}
