using Reliquary.Platform.Extensions;

namespace Reliquary.Platform.Tests.Extensions;

public sealed class PluginFramePolicyTests
{
    private static readonly Uri Files = new("https://example.org/api/v1/plugins/hello/1.0.0/files/");

    private static readonly string[] IncorrectOrigins =
    [
        "https://api.example.com; script-src *",
        "https://api.example.com 'unsafe-eval'",
        "*",
        "https://*.com",
        "http://api.example.com",
        "https://api.example.com/path",
        "data:",
        "'self'",
    ];

    [Fact]
    public void PolicyOfAPluginHasOnlyItsFilesAndItsNetwork()
    {
        var policy = PluginFramePolicy.ForPlugin(Files, ["https://api.example.com", "http://localhost:5000"]);

        Assert.Equal(
            "default-src 'none'; "
            + "script-src https://example.org/api/v1/plugins/hello/1.0.0/files/; "
            + "style-src https://example.org/api/v1/plugins/hello/1.0.0/files/ 'unsafe-inline'; "
            + "font-src https://example.org/api/v1/plugins/hello/1.0.0/files/; "
            + "img-src https://example.org/api/v1/plugins/hello/1.0.0/files/ https://api.example.com http://localhost:5000 data: blob:; "
            + "connect-src https://example.org/api/v1/plugins/hello/1.0.0/files/ https://api.example.com http://localhost:5000; "
            + "worker-src blob:; base-uri 'none'; form-action 'none'",
            policy);
    }

    [Fact]
    public void PolicyIgnoresAnOriginThatDoesNotFollowTheRuleOfTheManifest()
    {
        var policy = PluginFramePolicy.ForPlugin(Files, [.. IncorrectOrigins, "https://api.example.com", "https://api.example.com"]);

        Assert.Equal(
            "connect-src https://example.org/api/v1/plugins/hello/1.0.0/files/ https://api.example.com",
            policy.Split("; ").Single(directive => directive.StartsWith("connect-src", StringComparison.Ordinal)));
        Assert.DoesNotContain("*", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.org/api/v1/plugins/hello/1.0.0/files")]
    [InlineData("https://example.org/api/v1/plugins/hello/1.0.0/files/?a=b")]
    [InlineData("https://user@example.org/files/")]
    [InlineData("ftp://example.org/files/")]
    public void FilesUrlMustBeAnHttpFolder(string url)
    {
        Assert.Throws<ArgumentException>(() => PluginFramePolicy.ForPlugin(new Uri(url), []));
    }

    [Fact]
    public void PolicyOfADevelopmentPluginAllowsTheOriginOfItsServer()
    {
        var policy = PluginFramePolicy.ForDevelopment(
            new Uri("http://localhost:5173/src/main.ts"),
            [new Uri("https://api.example.com/v1/cards"), new Uri("https://api.example.com/other")]);

        Assert.Equal(
            "default-src 'none'; script-src http://localhost:5173; style-src http://localhost:5173 'unsafe-inline'; font-src http://localhost:5173; "
            + "img-src http://localhost:5173 https://api.example.com data: blob:; connect-src http://localhost:5173 ws://localhost:5173 https://api.example.com; "
            + "worker-src blob:; base-uri 'none'; form-action 'none'",
            policy);
    }

    [Fact]
    public void DevelopmentPluginOnHttpsReloadsThroughASecureSocket()
    {
        var policy = PluginFramePolicy.ForDevelopment(new Uri("https://dev.example.org:8443/index.js"), []);

        Assert.Contains("connect-src https://dev.example.org:8443 wss://dev.example.org:8443;", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionsPolicyTurnsOffAllTheFeaturesWithNoAcceptedDevice()
    {
        var policy = PluginFramePolicy.PermissionsPolicy([]);

        Assert.Equal(PluginFramePolicy.RestrictedFeatures.Select(feature => $"{feature}=()"), policy.Split(", "));
    }

    [Fact]
    public void PermissionsPolicyKeepsTheFeaturesOfTheAcceptedDevices()
    {
        var camera = PluginFramePolicy.PermissionsPolicy(["camera", "teleporter"]).Split(", ");
        var loopback = PluginFramePolicy.PermissionsPolicy(["loopback-network"]).Split(", ");

        Assert.DoesNotContain("camera=()", camera);
        Assert.Contains("microphone=()", camera);
        Assert.Contains("loopback-network=()", camera);
        Assert.Equal(PluginFramePolicy.RestrictedFeatures.Count - 1, camera.Length);
        Assert.DoesNotContain("loopback-network=()", loopback);
        Assert.DoesNotContain("local-network-access=()", loopback);
        Assert.Contains("local-network=()", loopback);
        Assert.Contains("camera=()", loopback);
    }

    [Fact]
    public void EachDeviceOfTheManifestHasItsFeatures()
    {
        Assert.Equal(PluginManifestRules.Devices.Order(StringComparer.Ordinal), PluginFramePolicy.DeviceFeatures.Keys.Order(StringComparer.Ordinal));
        Assert.All(PluginFramePolicy.DeviceFeatures.Values.SelectMany(features => features), feature => Assert.Contains(feature, PluginFramePolicy.RestrictedFeatures));
    }
}
