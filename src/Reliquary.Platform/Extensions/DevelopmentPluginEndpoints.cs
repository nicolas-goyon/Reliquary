using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// The frame of a development plugin: a plugin that a user loads from a URL on a development instance.
/// See docs/architecture/extensions.md.
/// </summary>
public static class DevelopmentPluginEndpoints
{
    /// <summary>The maximum number of network origins of a development plugin.</summary>
    public const int MaxNetworkOrigins = 20;

    /// <summary>
    /// Adds the frame endpoint of development plugins to the public API.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    public static void MapDevelopmentPluginEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/plugins/development/frame", Frame)
            .WithTags("Extensions")
            .WithName("DevelopmentPluginFrame")
            .WithSummary("Sends the HTML of the frame of a development plugin. The CSP allows the origin of the entry point. The instance must allow development plugins.");
    }

    private static Results<ContentHttpResult, ProblemHttpResult> Frame(
        string? entry, string[]? network, IOptions<PluginOptions> options, HttpContext context)
    {
        if (!options.Value.AllowDevelopmentPlugins)
        {
            return ApiProblems.NotFound("This instance does not allow development plugins. Set Plugins:AllowDevelopmentPlugins to true on a development instance.");
        }

        if (!TryParseHttpUri(entry, out var entryUri))
        {
            return ApiProblems.Validation(new FieldError("entry", FieldErrorCodes.InvalidFormat, "The entry point must be an absolute http or https address."));
        }

        network ??= [];
        if (network.Length > MaxNetworkOrigins)
        {
            return ApiProblems.Validation(new FieldError("network", FieldErrorCodes.TooLong, $"The network list must have {MaxNetworkOrigins} origins or fewer."));
        }

        var networkUris = new List<Uri>();
        for (var index = 0; index < network.Length; index++)
        {
            if (!TryParseHttpUri(network[index], out var uri))
            {
                return ApiProblems.Validation(new FieldError($"network[{index}]", FieldErrorCodes.InvalidFormat, "Each network origin must be an absolute http or https address."));
            }

            networkUris.Add(uri);
        }

        context.Response.Headers.ContentSecurityPolicy = PluginFramePolicy.ForDevelopment(entryUri, networkUris);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        var html = $"""
            <!doctype html>
            <html>
            <head><meta charset="utf-8"><title>Development plugin</title></head>
            <body><script type="module" src="{WebUtility.HtmlEncode(entryUri.AbsoluteUri)}"></script></body>
            </html>
            """;
        return TypedResults.Content(html, "text/html", Encoding.UTF8);
    }

    /// <summary>Accepts only an absolute http or https address with no user information.</summary>
    private static bool TryParseHttpUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 || !Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if ((parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
