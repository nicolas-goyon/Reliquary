using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Reliquary.Platform.Extensions;

namespace Reliquary.Api.Tests.Plugins;

public sealed class PluginFrameTests
{
    private const string Frame = "/api/v1/plugins/hello/1.0.0/frame";

    private const string Files = "http://localhost/api/v1/plugins/hello/1.0.0/files/";

    private static readonly byte[] WasmHeader = [0, 97, 115, 109];

    private static readonly string[] NoNetwork = [];

    private static readonly string[] ApiNetwork = ["https://api.example.com"];

    private static readonly string[] WideNetwork = ["https://*.example.com:8443", "http://localhost:5000", "http://127.0.0.1"];

    public static TheoryData<string[], string> Policies => new()
    {
        {
            NoNetwork,
            $"default-src 'none'; script-src {Files}; style-src {Files} 'unsafe-inline'; font-src {Files}; img-src {Files} data: blob:; connect-src {Files}; worker-src blob:; base-uri 'none'; form-action 'none'"
        },
        {
            ApiNetwork,
            $"default-src 'none'; script-src {Files}; style-src {Files} 'unsafe-inline'; font-src {Files}; img-src {Files} https://api.example.com data: blob:; connect-src {Files} https://api.example.com; worker-src blob:; base-uri 'none'; form-action 'none'"
        },
        {
            WideNetwork,
            $"default-src 'none'; script-src {Files}; style-src {Files} 'unsafe-inline'; font-src {Files}; img-src {Files} https://*.example.com:8443 http://localhost:5000 http://127.0.0.1 data: blob:; connect-src {Files} https://*.example.com:8443 http://localhost:5000 http://127.0.0.1; worker-src blob:; base-uri 'none'; form-action 'none'"
        },
    };

    [Theory]
    [MemberData(nameof(Policies))]
    public async Task FrameHasTheContentSecurityPolicyOfThePlugin(string[] network, string policy)
    {
        await using var instance = await PluginInstance.CreateAsync();
        await instance.InstallAsync(PluginInstance.Manifest(("network", network)));

        var response = await instance.Factory.CreateClient().GetAsync(new Uri(Frame, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(policy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task FrameLoadsTheEntryWithItsIntegrityHash()
    {
        await using var instance = await PluginInstance.CreateAsync();
        await instance.InstallAsync(PluginInstance.Manifest(("name", "Hello <b>")));
        var integrity = "sha384-" + Convert.ToBase64String(SHA384.HashData(PluginInstance.EntryContent));

        var response = await instance.Factory.CreateClient().GetAsync(new Uri(Frame, UriKind.Relative), TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"""<script type="module" src="files/dist/index.js" integrity="{integrity}" crossorigin="anonymous"></script>""", html, StringComparison.Ordinal);
        Assert.Contains("<title>Hello &lt;b&gt;</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrameUsesTheSchemeOfTheProxy()
    {
        await using var instance = await PluginInstance.CreateAsync();
        await instance.InstallAsync(PluginInstance.Manifest());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Frame, UriKind.Relative));
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await instance.Factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.StartsWith("default-src 'none'; script-src https://localhost/api/v1/plugins/hello/1.0.0/files/;", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileHasItsContentTypeAndTheHeadersOfAFrameWithAnOpaqueOrigin()
    {
        await using var instance = await PluginInstance.CreateAsync();
        var style = Encoding.UTF8.GetBytes("body { margin: 0 }");
        await instance.InstallAsync(
            PluginInstance.Manifest(),
            new PluginFile("dist/index.js", PluginInstance.EntryContent),
            new PluginFile("dist/style.css", style),
            new PluginFile("assets/icon.svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")),
            new PluginFile("assets/ocr.wasm", WasmHeader));
        var client = instance.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/plugins/hello/1.0.0/files/dist/index.js", UriKind.Relative));
        request.Headers.Add("Origin", "null");

        var script = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var css = await client.GetAsync(new Uri("/api/v1/plugins/hello/1.0.0/files/dist/style.css", UriKind.Relative), TestContext.Current.CancellationToken);
        var svg = await client.GetAsync(new Uri("/api/v1/plugins/hello/1.0.0/files/assets/icon.svg", UriKind.Relative), TestContext.Current.CancellationToken);
        var wasm = await client.GetAsync(new Uri("/api/v1/plugins/hello/1.0.0/files/assets/ocr.wasm", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal(PluginInstance.EntryContent, await script.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/javascript; charset=utf-8", script.Content.Headers.ContentType!.ToString());
        Assert.Equal("*", Assert.Single(script.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("sandbox; default-src 'none'", Assert.Single(script.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(script.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("text/css; charset=utf-8", css.Content.Headers.ContentType!.ToString());
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType!.ToString());
        Assert.Equal("sandbox; default-src 'none'", Assert.Single(svg.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("application/wasm", wasm.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task FileGivesNotModifiedForTheSameIntegrity()
    {
        await using var instance = await PluginInstance.CreateAsync();
        await instance.InstallAsync(PluginInstance.Manifest());
        var client = instance.Factory.CreateClient();
        var first = await client.GetAsync(new Uri("/api/v1/plugins/hello/1.0.0/files/dist/index.js", UriKind.Relative), TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/plugins/hello/1.0.0/files/dist/index.js", UriKind.Relative));
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        var second = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal($"\"sha384-{Convert.ToBase64String(SHA384.HashData(PluginInstance.EntryContent))}\"", first.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/plugins/hello/1.0.0/files/dist/missing.js")]
    [InlineData("/api/v1/plugins/hello/1.0.0/files/dist/.hidden.js")]
    [InlineData("/api/v1/plugins/hello/1.0.0/files/dist/%2E%2E/index.js")]
    [InlineData("/api/v1/plugins/hello/1.0.0/files/dist%5Cindex.js")]
    [InlineData("/api/v1/plugins/hello/0.9.0/files/dist/index.js")]
    [InlineData("/api/v1/plugins/hello/0.9.0/frame")]
    [InlineData("/api/v1/plugins/other/1.0.0/frame")]
    public async Task UnknownFileOrVersionIsNotFound(string path)
    {
        await using var instance = await PluginInstance.CreateAsync();
        await instance.InstallAsync(PluginInstance.Manifest());

        var response = await instance.Factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DisabledPluginHasNoFrameAndNoFiles()
    {
        await using var instance = await PluginInstance.CreateAsync();
        using var admin = await instance.InstallAsync(PluginInstance.Manifest());
        (await admin.PatchAsJsonAsync("/api/v1/admin/extensions/hello", new { enabled = false }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var client = instance.Factory.CreateClient();

        var frame = await client.GetAsync(new Uri(Frame, UriKind.Relative), TestContext.Current.CancellationToken);
        var file = await client.GetAsync(new Uri("/api/v1/plugins/hello/1.0.0/files/dist/index.js", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, frame.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
    }

    [Fact]
    public async Task FrameNeedsTheEntryFile()
    {
        await using var instance = await PluginInstance.CreateAsync();
        await instance.InstallAsync(PluginInstance.Manifest(), new PluginFile("dist/other.js", PluginInstance.EntryContent));

        var frame = await instance.Factory.CreateClient().GetAsync(new Uri(Frame, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, frame.StatusCode);
    }

    [Fact]
    public async Task FilesOfAVersionDoNotChange()
    {
        await using var instance = await PluginInstance.CreateAsync();
        await instance.InstallAsync(PluginInstance.Manifest());

        var result = await instance.Files.SaveVersionAsync("hello", "1.0.0", [new PluginFile("dist/index.js", Encoding.UTF8.GetBytes("alert(1)"))], TestContext.Current.CancellationToken);
        var file = await instance.Files.FindAsync("hello", "1.0.0", "dist/index.js", TestContext.Current.CancellationToken);

        Assert.Equal(PluginFileSaveResult.VersionExists, result);
        Assert.Equal(PluginInstance.EntryContent, file!.Content);
    }

    [Fact]
    public async Task ManifestWithoutKindIsAPluginManifest()
    {
        await using var instance = await PluginInstance.CreateAsync();
        using var admin = await instance.SignInAsync(PluginInstance.AdministratorEmail);

        var install = await admin.PutAsJsonAsync("/api/v1/admin/extensions/hello", PluginInstance.Manifest(), TestContext.Current.CancellationToken);
        var refused = await admin.PutAsJsonAsync("/api/v1/admin/extensions/hello", PluginInstance.Manifest(("author", "Somebody")), TestContext.Current.CancellationToken);
        var body = await install.Content.ReadFromJsonAsync<AdminExtensionResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        Assert.Equal("plugin", body!.Extension.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }
}
