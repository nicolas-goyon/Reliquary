using System.Net;
using System.Net.Http.Json;

namespace Reliquary.Api.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LivenessEndpointReturnsHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StatusEndpointListsTheCoreComponents()
    {
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<StatusResponse>(new Uri("/api/v1/status", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal("Reliquary", status.Name);
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
        Assert.Equal(["Accounts", "Cards", "Decks", "Collection", "Playtest"], status.Components);
    }

    [Fact]
    public async Task OpenApiDocumentDescribesTheStatusEndpoint()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/v1/status", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRouteReturnsProblemDetails()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/unknown", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed record StatusResponse(string Name, string Version, IReadOnlyList<string> Components);
}
