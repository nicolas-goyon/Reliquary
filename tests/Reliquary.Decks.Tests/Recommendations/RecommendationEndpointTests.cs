using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.Decks.Api;
using Reliquary.Decks.Recommendations;
using StackExchange.Redis;
using static Reliquary.Decks.Tests.Recommendations.RecommendationSample;

namespace Reliquary.Decks.Tests.Recommendations;

[Collection(DecksGroup.Name)]
public sealed class RecommendationEndpointTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EndpointGivesTheRecommendationsFromTheCacheWithoutTheCardsOfTheDeck()
    {
        var handler = new RecordedHandler(Json);
        await using var api = (await instance.GetAsync()).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recommendations:Enabled", "true");
            builder.ConfigureTestServices(services => services
                .AddHttpClient(RecommanderSource.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        var redis = api.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await redis.KeyDeleteAsync(RecommendationService.CacheKey("recommander", [Atraxa]));
        using var owner = await SignInAsync(api);
        using var other = await SignInAsync(api);

        var created = await owner.PostAsJsonAsync("/api/v1/decks", new
        {
            name = "Atraxa",
            lines = new object[]
            {
                new { name = "Atraxa, Praetors' Voice", zone = "commander" },
                new { name = "Sol Ring" },
                new { name = "Counterspell", zone = "considering" },
            },
        }, Cancel);
        created.EnsureSuccessStatusCode();
        var deck = await created.Content.ReadFromJsonAsync<DeckResponse>(Cancel);
        var path = new Uri($"/api/v1/decks/{deck!.Id}/recommendations", UriKind.Relative);

        var first = await owner.GetFromJsonAsync<DeckRecommendationsResponse>(path, Cancel);
        var watch = Stopwatch.StartNew();
        var second = await owner.GetFromJsonAsync<DeckRecommendationsResponse>(path, Cancel);
        watch.Stop();
        var forOther = await other.GetAsync(path, Cancel);
        var badLimit = await owner.GetAsync(new Uri($"{path}?limit=0", UriKind.Relative), Cancel);

        Assert.Equal(1, handler.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"The cached request took {watch.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.Equal(RecommendationCodes.Ok, first!.Status);
        Assert.Equal([Atraxa], first.Commanders);
        Assert.Equal([CommandTower, Cultivate, Swords, LlanowarElves], first.Cards.Select(card => card.OracleId));
        Assert.Equal(first.Cards.Select(card => card.OracleId), second!.Cards.Select(card => card.OracleId));
        Assert.Equal("Recommander", first.Source.Name);
        Assert.Equal("Command Tower", first.Cards[0].Name);
        Assert.NotNull(first.Cards[0].TypeLine);
        Assert.Equal(HttpStatusCode.NotFound, forOther.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
    }

    [Fact]
    public async Task EndpointGivesNotFoundWhenTheOptionIsOff()
    {
        using var owner = await instance.NewUserAsync();
        var created = await owner.PostAsJsonAsync("/api/v1/decks", new { name = "No recommendations" }, Cancel);
        created.EnsureSuccessStatusCode();
        var deck = await created.Content.ReadFromJsonAsync<DeckResponse>(Cancel);

        var response = await owner.GetAsync(new Uri($"/api/v1/decks/{deck!.Id}/recommendations", UriKind.Relative), Cancel);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RecommendationCodes.Disabled, problem.GetProperty("code").GetString());
    }

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> api)
    {
        var client = api.CreateClient();
        var email = $"recommendations-{Guid.NewGuid():N}@example.org";
        var credentials = new { email, password = "correct horse battery", displayName = "Tester", language = "en" };
        (await client.PostAsJsonAsync("/api/v1/auth/register", credentials, Cancel)).EnsureSuccessStatusCode();
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", credentials, Cancel);
        signIn.EnsureSuccessStatusCode();
        var session = await signIn.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }
}
