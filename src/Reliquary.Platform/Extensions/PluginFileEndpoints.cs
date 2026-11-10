using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Platform.Extensions;

/// <summary>
/// The endpoints that send the frame and the files of an installed plugin version. See docs/decisions/0049-plugin-sandbox.md.
/// </summary>
/// <remarks>
/// The two endpoints accept anonymous requests: the browser sends no credential when it opens the frame,
/// and a request from a frame with an opaque origin has no cookie. The plugin files are not secret.
/// The endpoints send only the files of the installed version, when the instance enables the plugin.
/// </remarks>
public static class PluginFileEndpoints
{
    /// <summary>
    /// Adds the frame endpoint and the file endpoint of the plugins to the public API.
    /// </summary>
    /// <param name="api">The route group of the public API.</param>
    public static void MapPluginFileEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/plugins/{id}/{version}/frame", FrameAsync)
            .WithTags("Extensions")
            .WithName("PluginFrame")
            .WithSummary("Sends the HTML of the frame of a plugin version, with the Content Security Policy of this plugin.");

        api.MapGet("/plugins/{id}/{version}/files/{**path}", FileAsync)
            .WithTags("Extensions")
            .WithName("PluginFile")
            .WithSummary("Sends a file of a plugin version. A frame with an opaque origin can load it.");
    }

    /// <summary>
    /// Gets the absolute URL of the folder of the files of a plugin version, on the host of the request.
    /// </summary>
    /// <param name="request">The request of the frame. With the setting Plugins:FrameOrigin, it is the second host name of the frames.</param>
    /// <param name="id">The ID of the plugin.</param>
    /// <param name="version">The version of the plugin.</param>
    /// <returns>The URL, with a slash at the end.</returns>
    public static Uri FilesUrl(HttpRequest request, string id, string version)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nginx of the web image sends the scheme of the browser in X-Forwarded-Proto. An http source of a CSP also accepts https.
        var forwarded = request.Headers["X-Forwarded-Proto"].ToString();
        var scheme = forwarded is "https" or "http" ? forwarded : request.Scheme;
        var builder = new UriBuilder(scheme, request.Host.Host, request.Host.Port ?? -1, $"{ApiRoutes.Base}/plugins/{id}/{version}/files/");
        return builder.Uri;
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> FrameAsync(
        string id, string version, HttpContext context, ExtensionRegistry registry, PluginFileStore files, CancellationToken cancellationToken)
    {
        if (await FindVersionAsync(id, version, registry, cancellationToken) is not { } plugin)
        {
            return NoVersion();
        }

        if (await files.IntegrityAsync(id, version, plugin.Entry, cancellationToken) is not { } integrity)
        {
            return ApiProblems.NotFound("The instance does not have the entry file of this plugin version. Wait for the next sync of the plugin catalog.");
        }

        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = PluginFramePolicy.ForPlugin(FilesUrl(context.Request, id, version), plugin.Network);
        headers.CacheControl = "no-store";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";

        // The address of the entry is relative to the frame: /api/v1/plugins/{id}/{version}/files/{entry}.
        var html = $"""
            <!doctype html>
            <html>
            <head><meta charset="utf-8"><title>{WebUtility.HtmlEncode(plugin.Name)}</title></head>
            <body><script type="module" src="files/{WebUtility.HtmlEncode(plugin.Entry)}" integrity="{WebUtility.HtmlEncode(integrity)}" crossorigin="anonymous"></script></body>
            </html>
            """;
        return TypedResults.Content(html, "text/html", Encoding.UTF8);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> FileAsync(
        string id, string version, string? path, HttpContext context, ExtensionRegistry registry, PluginFileStore files, CancellationToken cancellationToken)
    {
        if (!PluginFileStore.IsValidPath(path) || await FindVersionAsync(id, version, registry, cancellationToken) is null)
        {
            return NoVersion();
        }

        if (await files.FindAsync(id, version, path!, cancellationToken) is not { } file)
        {
            return ApiProblems.NotFound("This plugin version has no file with this path.");
        }

        var headers = context.Response.Headers;

        // A frame with an opaque origin sends "Origin: null". A script with an integrity hash needs this header.
        headers.AccessControlAllowOrigin = "*";

        // A browser that opens the file directly gets an opaque origin, so a file of a plugin cannot run on the origin of the instance.
        headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        headers.XContentTypeOptions = "nosniff";

        // The files of a version do not change. The frame endpoint does not use the cache, so a disabled version stops at the next start of the frame.
        headers.CacheControl = "public, max-age=86400";
        return TypedResults.Bytes(file.Content, file.ContentType, entityTag: new EntityTagHeaderValue($"\"{file.Integrity}\""));
    }

    /// <summary>
    /// Finds the plugin version that the endpoints can send: the installed version of a plugin that the instance enables and that supports the core API.
    /// </summary>
    private static async Task<FramePlugin?> FindVersionAsync(string id, string version, ExtensionRegistry registry, CancellationToken cancellationToken)
    {
        var row = await registry.FindAsync(id, cancellationToken);
        if (row is not { Kind: "plugin", Enabled: true, Compatible: true } || row.Version != version)
        {
            return null;
        }

        using var manifest = JsonDocument.Parse(row.Manifest);
        var root = manifest.RootElement;
        var entry = root.TryGetProperty("entry", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (!PluginFileStore.IsValidPath(entry))
        {
            return null;
        }

        var network = root.TryGetProperty("network", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
            : [];
        return new FramePlugin(row.Name, entry!, network);
    }

    private static ProblemHttpResult NoVersion() =>
        ApiProblems.NotFound("The instance has no enabled plugin with this ID and this version.");

    private sealed record FramePlugin(string Name, string Entry, IReadOnlyList<string> Network);
}
