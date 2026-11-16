using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Reliquary.Platform.Extensions;

namespace Reliquary.Api.Tests.Plugins;

public sealed class PluginFrameTests(PluginInstanceFixture fixture) : IClassFixture<PluginInstanceFixture>
{
    private static readonly byte[] WasmHeader = [0, 97, 115, 109];

    private static readonly string[] NoNetwork = [];

    private static readonly string[] ApiNetwork = ["https://api.example.com"];

    private static readonly string[] WideNetwork = ["https://*.example.com:8443", "http://localhost:5000", "http://127.0.0.1"];

    private PluginInstance Instance => fixture.Instance;

    /// <summary>The expected policies. <c>{0}</c> is the URL of the folder of the plugin files.</summary>
    public static TheoryData<string[], string> Policies => new()
    {
        {
            NoNetwork,
            "default-src 'none'; script-src {0}; style-src {0} 'unsafe-inline'; font-src {0}; img-src {0} data: blob:; connect-src {0}; worker-src blob:; base-uri 'none'; form-action 'none'"
        },
        {
            ApiNetwork,
            "default-src 'none'; script-src {0}; style-src {0} 'unsafe-inline'; font-src {0}; img-src {0} https://api.example.com data: blob:; connect-src {0} https://api.example.com; worker-src blob:; base-uri 'none'; form-action 'none'"
        },
        {
            WideNetwork,
            "default-src 'none'; script-src {0}; style-src {0} 'unsafe-inline'; font-src {0}; img-src {0} https://*.example.com:8443 http://localhost:5000 http://127.0.0.1 data: blob:; connect-src {0} https://*.example.com:8443 http://localhost:5000 http://127.0.0.1; worker-src blob:; base-uri 'none'; form-action 'none'"
        },
    };

    [Theory]
    [MemberData(nameof(Policies))]
    public async Task FrameHasTheContentSecurityPolicyOfThePlugin(string[] network, string policy)
    {
        var id = await InstallAsync(("network", network));

        var response = await GetAsync(Frame(id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Format(null, policy, Files(id)), Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task FrameLoadsTheEntryWithItsIntegrityHash()
    {
        var id = await InstallAsync(("name", "Hello <b>"));
        var integrity = "sha384-" + Convert.ToBase64String(SHA384.HashData(PluginInstance.EntryContent));

        var response = await GetAsync(Frame(id));
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"""<script type="module" src="files/dist/index.js" integrity="{integrity}" crossorigin="anonymous"></script>""", html, StringComparison.Ordinal);
        Assert.Contains("<title>Hello &lt;b&gt;</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrameUsesTheSchemeOfTheProxy()
    {
        var id = await InstallAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Frame(id), UriKind.Relative));
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await Instance.Factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.StartsWith($"default-src 'none'; script-src https://localhost/api/v1/plugins/{id}/1.0.0/files/;", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileHasItsContentTypeAndTheHeadersOfAFrameWithAnOpaqueOrigin()
    {
        var id = PluginInstance.NewId();
        await Instance.InstallAsync(
            PluginInstance.Manifest(("id", id)),
            new PluginFile("dist/index.js", PluginInstance.EntryContent),
            new PluginFile("dist/style.css", Encoding.UTF8.GetBytes("body { margin: 0 }")),
            new PluginFile("assets/icon.svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")),
            new PluginFile("assets/ocr.wasm", WasmHeader));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(File(id, "dist/index.js"), UriKind.Relative));
        request.Headers.Add("Origin", "null");

        var script = await Instance.Factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        var css = await GetAsync(File(id, "dist/style.css"));
        var svg = await GetAsync(File(id, "assets/icon.svg"));
        var wasm = await GetAsync(File(id, "assets/ocr.wasm"));

        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal(PluginInstance.EntryContent, await script.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/javascript; charset=utf-8", script.Content.Headers.ContentType!.ToString());
        Assert.Equal("*", Assert.Single(script.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(PluginFramePolicy.ForFile, Assert.Single(script.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("sandbox; default-src 'none'", PluginFramePolicy.ForFile);
        Assert.Equal("nosniff", Assert.Single(script.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("text/css; charset=utf-8", css.Content.Headers.ContentType!.ToString());
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType!.ToString());
        Assert.Equal(PluginFramePolicy.ForFile, Assert.Single(svg.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("application/wasm", wasm.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task FileGivesNotModifiedForTheSameIntegrity()
    {
        var id = await InstallAsync();
        var first = await GetAsync(File(id, "dist/index.js"));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(File(id, "dist/index.js"), UriKind.Relative));
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        var second = await Instance.Factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal($"\"sha384-{Convert.ToBase64String(SHA384.HashData(PluginInstance.EntryContent))}\"", first.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    /// <summary>The paths of requests that must give 404. <c>{0}</c> is the ID of the installed plugin.</summary>
    [Theory]
    [InlineData("/api/v1/plugins/{0}/1.0.0/files/dist/missing.js")]
    [InlineData("/api/v1/plugins/{0}/1.0.0/files/dist/.hidden.js")]
    [InlineData("/api/v1/plugins/{0}/1.0.0/files/dist/%2E%2E/index.js")]
    [InlineData("/api/v1/plugins/{0}/1.0.0/files/dist%5Cindex.js")]
    [InlineData("/api/v1/plugins/{0}/0.9.0/files/dist/index.js")]
    [InlineData("/api/v1/plugins/{0}/0.9.0/frame")]
    [InlineData("/api/v1/plugins/not-installed/1.0.0/frame")]
    public async Task UnknownFileOrVersionIsNotFound(string path)
    {
        var id = await InstallAsync();

        var response = await GetAsync(string.Format(null, path, id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DisabledPluginHasNoFrameAndNoFiles()
    {
        var id = await InstallAsync();
        var admin = await Instance.AdministratorAsync();
        (await admin.PatchAsJsonAsync($"/api/v1/admin/extensions/{id}", new { enabled = false }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var frame = await GetAsync(Frame(id));
        var file = await GetAsync(File(id, "dist/index.js"));

        Assert.Equal(HttpStatusCode.NotFound, frame.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
    }

    [Fact]
    public async Task FrameNeedsTheEntryFile()
    {
        var id = PluginInstance.NewId();
        await Instance.InstallAsync(PluginInstance.Manifest(("id", id)), new PluginFile("dist/other.js", PluginInstance.EntryContent));

        var frame = await GetAsync(Frame(id));

        Assert.Equal(HttpStatusCode.NotFound, frame.StatusCode);
    }

    [Fact]
    public async Task FilesOfAVersionDoNotChange()
    {
        var id = await InstallAsync();

        var result = await Instance.Files.SaveVersionAsync(id, "1.0.0", [new PluginFile("dist/index.js", Encoding.UTF8.GetBytes("alert(1)"))], TestContext.Current.CancellationToken);
        var file = await Instance.Files.FindAsync(id, "1.0.0", "dist/index.js", TestContext.Current.CancellationToken);

        Assert.Equal(PluginFileSaveResult.VersionExists, result);
        Assert.Equal(PluginInstance.EntryContent, file!.Content);
    }

    [Fact]
    public async Task ManifestWithoutKindIsAPluginManifest()
    {
        var id = PluginInstance.NewId();
        var admin = await Instance.AdministratorAsync();

        var install = await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", PluginInstance.Manifest(("id", id)), TestContext.Current.CancellationToken);
        var refused = await admin.PutAsJsonAsync($"/api/v1/admin/extensions/{id}", PluginInstance.Manifest(("id", id), ("author", "Somebody")), TestContext.Current.CancellationToken);
        var body = await install.Content.ReadFromJsonAsync<AdminExtensionResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, install.StatusCode);
        Assert.Equal("plugin", body!.Extension.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    private static string Frame(string id) => $"/api/v1/plugins/{id}/1.0.0/frame";

    private static string Files(string id) => $"http://localhost/api/v1/plugins/{id}/1.0.0/files/";

    private static string File(string id, string path) => $"/api/v1/plugins/{id}/1.0.0/files/{path}";

    /// <summary>Installs a plugin with a new ID and its entry file.</summary>
    private async Task<string> InstallAsync(params (string Key, object? Value)[] fields)
    {
        var id = PluginInstance.NewId();
        await Instance.InstallAsync(PluginInstance.Manifest([("id", id), .. fields]));
        return id;
    }

    private Task<HttpResponseMessage> GetAsync(string path) =>
        Instance.Factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
}
