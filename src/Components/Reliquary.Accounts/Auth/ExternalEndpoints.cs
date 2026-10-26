using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.Accounts.Security;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Accounts.Auth;

/// <summary>The OAuth providers that the instance enables.</summary>
/// <param name="Providers">The names of the providers, for example <c>github</c>.</param>
public sealed record ProvidersResponse(IReadOnlyList<string> Providers);

/// <summary>
/// The endpoints of the sign-in with an OAuth provider: Google, GitHub or Discord.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The web interface opens <c>GET /auth/external/{provider}</c>. The API sends the browser to the provider.</item>
/// <item>The provider sends the browser back to the callback path. The API keeps the result in a short cookie.</item>
/// <item>The API completes the sign-in, writes the refresh cookie and sends the browser to the page <c>/auth/complete</c> of the web interface.</item>
/// <item>That page gets an access token from <c>POST /auth/refresh</c>.</item>
/// </list>
/// </remarks>
internal static class ExternalEndpoints
{
    private const string CompletePath = ApiRoutes.Base + "/auth/external/complete";
    private const string ProviderItem = "provider";
    private const string ReturnUrlItem = "returnUrl";
    private const string LanguageItem = "language";

    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication");

        auth.MapGet("/providers", (EnabledProviders providers) => TypedResults.Ok(new ProvidersResponse(providers.Names)))
            .WithName("ListProviders")
            .WithSummary("Lists the OAuth providers that the instance enables.");

        auth.MapGet("/external/complete", CompleteAsync)
            .WithName("CompleteExternalSignIn")
            .WithSummary("Completes the sign-in with an OAuth provider. The browser comes here after the provider. Do not call it directly.")
            .ExcludeFromDescription();

        auth.MapGet("/external/{provider}", Challenge)
            .WithName("StartExternalSignIn")
            .WithSummary("Sends the browser to an OAuth provider. Open this address in the browser, not with a script.")
            .RequireRateLimiting(AuthEndpoints.RateLimiterPolicy);
    }

    private static Results<ChallengeHttpResult, ProblemHttpResult> Challenge(string provider, string? returnUrl, string? language, EnabledProviders providers)
    {
        if (!providers.Names.Contains(provider, StringComparer.Ordinal))
        {
            return ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                AccountErrorCodes.ProviderNotEnabled,
                $"The provider \"{provider}\" is not enabled on this instance. Use GET /auth/providers to get the enabled providers.");
        }

        var properties = new AuthenticationProperties { RedirectUri = CompletePath };
        properties.Items[ProviderItem] = provider;
        properties.Items[ReturnUrlItem] = SafeReturnUrl(returnUrl);
        properties.Items[LanguageItem] = language is not null && AccountValidation.Languages.Contains(language) ? language : "en";
        return TypedResults.Challenge(properties, [provider]);
    }

    private static async Task<RedirectHttpResult> CompleteAsync(
        HttpContext context,
        AccountService accounts,
        IOptions<AuthOptions> options,
        ILogger<AccountService> logger,
        CancellationToken cancellationToken)
    {
        var result = await context.AuthenticateAsync(AuthSchemes.External);
        await context.SignOutAsync(AuthSchemes.External);
        var properties = result.Properties;
        var provider = Item(properties, ProviderItem) ?? "unknown";
        var returnUrl = SafeReturnUrl(Item(properties, ReturnUrlItem));
        if (!result.Succeeded || result.Principal is null)
        {
            logger.ExternalSignInFailed(provider, result.Failure?.Message ?? "The external cookie is missing.");
            return Redirect(options.Value, returnUrl, AccountErrorCodes.ExternalSignInFailed);
        }

        var language = Item(properties, LanguageItem) ?? "en";
        var identity = ExternalClaims.Read(provider, result.Principal, language);
        var (session, error) = await accounts.ExternalSignInAsync(identity, cancellationToken);
        if (session is null)
        {
            logger.ExternalSignInFailed(provider, error ?? AccountErrorCodes.ExternalSignInFailed);
            return Redirect(options.Value, returnUrl, error ?? AccountErrorCodes.ExternalSignInFailed);
        }

        RefreshCookie.Write(context.Response, session);
        return Redirect(options.Value, returnUrl, null);
    }

    private static RedirectHttpResult Redirect(AuthOptions options, string returnUrl, string? error)
    {
        var target = $"{options.PublicUrl.ToString().TrimEnd('/')}/auth/complete?returnUrl={Uri.EscapeDataString(returnUrl)}";
        if (error is not null)
        {
            target += $"&error={Uri.EscapeDataString(error)}";
        }

        return TypedResults.Redirect(target);
    }

    private static string? Item(AuthenticationProperties? properties, string key) =>
        properties is not null && properties.Items.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Accepts only a local path, so the sign-in cannot send the user to another site.
    /// </summary>
    internal static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } value && value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\', StringComparison.Ordinal)
            ? value
            : "/";
}
