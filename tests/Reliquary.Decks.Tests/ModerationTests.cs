using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Decks.Api;
using Reliquary.Decks.Exploration;
using Reliquary.Decks.Moderation;

namespace Reliquary.Decks.Tests;

/// <summary>
/// The reports of the users and the moderation of the public content by the administrators.
/// </summary>
[Collection(DecksGroup.Name)]
public sealed class ModerationTests(DecksInstance instance)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserReportsAPublicDeckOneTime()
    {
        using var author = await instance.NewUserAsync();
        using var reader = await instance.NewUserAsync();
        var deck = await CreateDeckAsync(author, $"Reported {Guid.NewGuid():N}", "public");
        var secret = await CreateDeckAsync(author, "Secret", "unlisted");
        await instance.PublishEventsAsync();

        var first = await ReportAsync(reader, "deck", deck.Id, "spam", "  Advertisement in the description.  ");
        var report = await first.Content.ReadFromJsonAsync<ReportResponse>(Cancel);
        var second = await ReportAsync(reader, "deck", deck.Id, "offensive");
        var own = await ReportAsync(author, "deck", deck.Id, "spam");
        var unlisted = await ReportAsync(reader, "deck", secret.Id, "spam");
        var invalid = await ReportAsync(reader, "deck", deck.Id, "boring", new string('a', ModerationValues.MaxCommentLength + 1));
        using var visitor = (await instance.GetAsync()).CreateClient();
        var anonymous = await ReportAsync(visitor, "deck", deck.Id, "spam");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(("open", "spam", "Advertisement in the description."), (report!.Status, report.Reason, report.Comment));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ModerationErrorCodes.ReportExists, await CodeAsync(second));
        Assert.Equal(HttpStatusCode.BadRequest, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unlisted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(["comment", "reason"], (await FieldsAsync(invalid)).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task ReportsHaveALimitForEachUser()
    {
        using var author = await instance.NewUserAsync();
        using var reader = await instance.NewUserAsync();
        var decks = new List<DeckResponse>();
        for (var index = 0; index < DecksInstance.ReportsPerDay; index++)
        {
            decks.Add(await CreateDeckAsync(author, $"Deck {index}", "public"));
        }

        await instance.PublishEventsAsync();
        var statuses = new List<HttpStatusCode>();
        foreach (var deck in decks)
        {
            statuses.Add((await ReportAsync(reader, "deck", deck.Id, "other")).StatusCode);
        }

        var tooMany = await ReportAsync(reader, "author", decks[0].OwnerId, "spam");

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Created, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, tooMany.StatusCode);
        Assert.Equal(ErrorCodes.RateLimited, await CodeAsync(tooMany));
    }

    [Fact]
    public async Task OnlyAnAdministratorCanModerate()
    {
        using var user = await instance.NewUserAsync();
        using var visitor = (await instance.GetAsync()).CreateClient();
        using var administrator = await instance.AdministratorAsync();
        var deck = await CreateDeckAsync(user, "Mine", "public");

        var asUser = await user.GetAsync(new Uri("/api/v1/moderation/reports", UriKind.Relative), Cancel);
        var hideAsUser = await user.PutAsJsonAsync($"/api/v1/moderation/hidden/deck/{deck.Id}", new { reason = "Test" }, Cancel);
        var asVisitor = await visitor.GetAsync(new Uri("/api/v1/moderation/log", UriKind.Relative), Cancel);
        var asAdministrator = await administrator.GetAsync(new Uri("/api/v1/moderation/reports", UriKind.Relative), Cancel);
        var wrongType = await administrator.PutAsJsonAsync($"/api/v1/moderation/hidden/card/{deck.Id}", new { reason = "Test" }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, hideAsUser.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asVisitor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdministrator.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
    }

    [Fact]
    public async Task HiddenDeckLeavesTheSearchInLessThanTenSecondsAndOnlyTheOwnerSeesIt()
    {
        using var author = await instance.NewUserAsync();
        using var reader = await instance.NewUserAsync();
        using var administrator = await instance.AdministratorAsync();
        var name = $"Offensive {Guid.NewGuid():N}";
        var deck = await CreateDeckAsync(author, name, "public");
        await instance.PublishEventsAsync();
        Assert.Single(await SearchAsync(reader, $"q={name}"));
        var report = (await (await ReportAsync(reader, "deck", deck.Id, "offensive", "Bad name.")).Content.ReadFromJsonAsync<ReportResponse>(Cancel))!;
        var open = await ListAsync<ModerationReportResponse>(administrator, "/moderation/reports");
        var listed = Assert.Single(open, item => item.Id == report.Id);

        var watch = Stopwatch.StartNew();
        var hide = await administrator.PutAsJsonAsync($"/api/v1/moderation/hidden/deck/{deck.Id}", new { reason = "Offensive name" }, Cancel);
        var found = await SearchAsync(reader, $"q={name}");
        watch.Stop();

        var forReader = await reader.GetAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var forOwner = await author.GetFromJsonAsync<DeckResponse>(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel);
        var copy = await reader.PostAsJsonAsync($"/api/v1/decks/{deck.Id}/copy", new { }, Cancel);
        var social = await reader.GetFromJsonAsync<PublicDeckSocialResponse>(new Uri($"/api/v1/public-decks/{deck.Id}/social", UriKind.Relative), Cancel);
        var stillOpen = await ListAsync<ModerationReportResponse>(administrator, "/moderation/reports");
        var resolved = await ListAsync<ModerationReportResponse>(administrator, "/moderation/reports?status=resolved");
        var hidden = await ListAsync<HiddenItemResponse>(administrator, "/moderation/hidden");
        var log = await ListAsync<ModerationLogEntryResponse>(administrator, "/moderation/log");

        Assert.Equal((name, "Tester", "Tester", false), (listed.ItemName, listed.Owner.DisplayName, listed.Reporter.DisplayName, listed.ItemHidden));
        Assert.Equal(HttpStatusCode.NoContent, hide.StatusCode);
        Assert.Empty(found);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.NotFound, forReader.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, copy.StatusCode);
        Assert.True(forOwner!.Hidden);
        Assert.False(social!.Public);
        Assert.DoesNotContain(stillOpen, item => item.Id == report.Id);
        Assert.True(Assert.Single(resolved, item => item.Id == report.Id).ItemHidden);
        var hiddenDeck = Assert.Single(hidden, item => item.ItemId == deck.Id);
        Assert.Equal(("Offensive name", deck.OwnerId, name), (hiddenDeck.Reason, hiddenDeck.Owner.Id, hiddenDeck.ItemName));
        Assert.Contains(log, entry => entry.Action == "hide" && entry.ItemId == deck.Id && entry.Reason == "Offensive name" && entry.Moderator is not null);
        Assert.Contains(log, entry => entry.Action == "resolve" && entry.ReportId == report.Id);

        // A change of the owner sends a domain event. The read model keeps the deck out of the search.
        await author.PatchAsJsonAsync($"/api/v1/decks/{deck.Id}", new { description = "New text" }, Cancel);
        await instance.PublishEventsAsync();
        Assert.Empty(await SearchAsync(reader, $"q={name}"));

        var show = await administrator.DeleteAsync(new Uri($"/api/v1/moderation/hidden/deck/{deck.Id}?reason=Changed", UriKind.Relative), Cancel);
        var again = await administrator.DeleteAsync(new Uri($"/api/v1/moderation/hidden/deck/{deck.Id}", UriKind.Relative), Cancel);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (show.StatusCode, again.StatusCode));
        Assert.Single(await SearchAsync(reader, $"q={name}"));
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(new Uri($"/api/v1/decks/{deck.Id}", UriKind.Relative), Cancel)).StatusCode);
    }

    [Fact]
    public async Task HiddenAuthorProfileGivesNotFoundAndItsDecksLeaveTheSearch()
    {
        using var author = await instance.NewUserAsync();
        using var reader = await instance.NewUserAsync();
        using var administrator = await instance.AdministratorAsync();
        var name = $"Spam {Guid.NewGuid():N}";
        var deck = await CreateDeckAsync(author, name, "public");
        await instance.PublishEventsAsync();
        var follow = await reader.PutAsync(new Uri($"/api/v1/authors/{deck.OwnerId}/follow", UriKind.Relative), null, Cancel);

        var hide = await administrator.PutAsJsonAsync($"/api/v1/moderation/hidden/author/{deck.OwnerId}", new { reason = "Spam account" }, Cancel);
        var profile = await reader.GetAsync(new Uri($"/api/v1/authors/{deck.OwnerId}", UriKind.Relative), Cancel);
        var own = await author.GetFromJsonAsync<AuthorResponse>(new Uri($"/api/v1/authors/{deck.OwnerId}", UriKind.Relative), Cancel);
        var search = await SearchAsync(reader, $"q={name}");
        var byAuthor = await SearchAsync(reader, $"author={deck.OwnerId}");
        var followed = await reader.GetFromJsonAsync<List<AuthorSummaryResponse>>(new Uri("/api/v1/authors/following", UriKind.Relative), Cancel);
        var report = await ReportAsync(reader, "author", deck.OwnerId, "spam");
        var followAgain = await reader.PutAsync(new Uri($"/api/v1/authors/{deck.OwnerId}/follow", UriKind.Relative), null, Cancel);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (follow.StatusCode, hide.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, profile.StatusCode);
        Assert.True(own!.Hidden);
        Assert.Empty(search);
        Assert.Empty(byAuthor);
        Assert.DoesNotContain(followed!, item => item.Id == deck.OwnerId);
        Assert.Equal(HttpStatusCode.NotFound, report.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, followAgain.StatusCode);

        var show = await administrator.DeleteAsync(new Uri($"/api/v1/moderation/hidden/author/{deck.OwnerId}", UriKind.Relative), Cancel);
        Assert.Equal(HttpStatusCode.NoContent, show.StatusCode);
        Assert.Single(await SearchAsync(reader, $"q={name}"));
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(new Uri($"/api/v1/authors/{deck.OwnerId}", UriKind.Relative), Cancel)).StatusCode);
    }

    [Fact]
    public async Task AdministratorDismissesAReportOneTime()
    {
        using var author = await instance.NewUserAsync();
        using var reader = await instance.NewUserAsync();
        using var administrator = await instance.AdministratorAsync();
        var me = await author.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/me", UriKind.Relative), Cancel);
        var authorId = me.GetProperty("account").GetProperty("id").GetGuid();
        var report = (await (await ReportAsync(reader, "author", authorId, "other", "Strange name.")).Content.ReadFromJsonAsync<ReportResponse>(Cancel))!;

        var dismiss = await administrator.PostAsJsonAsync($"/api/v1/moderation/reports/{report.Id}/dismiss", new { reason = "No problem" }, Cancel);
        var again = await administrator.PostAsync(new Uri($"/api/v1/moderation/reports/{report.Id}/resolve", UriKind.Relative), null, Cancel);
        var unknown = await administrator.PostAsync(new Uri($"/api/v1/moderation/reports/{Guid.NewGuid()}/resolve", UriKind.Relative), null, Cancel);
        var dismissed = await ListAsync<ModerationReportResponse>(administrator, "/moderation/reports?status=dismissed");
        var log = await ListAsync<ModerationLogEntryResponse>(administrator, "/moderation/log");
        var second = await ReportAsync(reader, "author", authorId, "spam");

        Assert.Equal(HttpStatusCode.NoContent, dismiss.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ModerationErrorCodes.ReportClosed, await CodeAsync(again));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.NotNull(Assert.Single(dismissed, item => item.Id == report.Id).ClosedAt);
        Assert.Contains(log, entry => entry.Action == "dismiss" && entry.ReportId == report.Id && entry.Reason == "No problem");
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    private static Task<HttpResponseMessage> ReportAsync(HttpClient client, string itemType, Guid itemId, string reason, string? comment = null) =>
        client.PostAsJsonAsync("/api/v1/reports", new { itemType, itemId, reason, comment }, Cancel);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();

    private static async Task<List<string>> FieldsAsync(HttpResponseMessage response) =>
        [.. (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()!)];

    private static async Task<List<T>> ListAsync<T>(HttpClient client, string path) =>
        [.. (await client.GetFromJsonAsync<Page<T>>(new Uri($"/api/v1{path}{(path.Contains('?', StringComparison.Ordinal) ? '&' : '?')}limit=100", UriKind.Relative), Cancel))!.Items];

    private static async Task<List<PublicDeckResponse>> SearchAsync(HttpClient client, string query) =>
        [.. (await client.GetFromJsonAsync<Page<PublicDeckResponse>>(new Uri($"/api/v1/public-decks?{query}", UriKind.Relative), Cancel))!.Items];

    private static async Task<DeckResponse> CreateDeckAsync(HttpClient client, string name, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/v1/decks", new { name, visibility, lines = new[] { new { name = "Sol Ring" } } }, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        return (await response.Content.ReadFromJsonAsync<DeckResponse>(Cancel))!;
    }
}
