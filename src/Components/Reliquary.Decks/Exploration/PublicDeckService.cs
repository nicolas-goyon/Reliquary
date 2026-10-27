using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Exploration;

/// <summary>
/// The search of the public decks, the favorites and the authors.
/// </summary>
internal sealed class PublicDeckService(DeckStore store, ICardCatalog catalog, IAccountDirectory accounts, TimeProvider time)
{
    public async Task<IReadOnlyList<PublicDeckResponse>> SearchAsync(PublicDeckQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PublicDeckRow> rows;
        await using (var connection = await store.OpenUsersAsync(cancellationToken))
        {
            rows = await PublicDeckStore.SearchAsync(connection, query, cancellationToken);
        }

        return await ToResponsesAsync(rows, cancellationToken);
    }

    /// <summary>
    /// Gets the public decks of the authors that a user follows, the most recent change first.
    /// </summary>
    public async Task<IReadOnlyList<PublicDeckResponse>> ListFollowedDecksAsync(Guid userId, int offset, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> authors;
        await using (var connection = await store.OpenUsersAsync(cancellationToken))
        {
            authors = await PublicDeckStore.ListFollowedAsync(connection, userId, cancellationToken);
        }

        return await SearchAsync(new PublicDeckQuery(null, null, null, null, null, authors, null, "recent", offset, limit), cancellationToken);
    }

    public async Task<IReadOnlyList<AuthorSummaryResponse>> ListFollowedAuthorsAsync(Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> authors;
        await using (var connection = await store.OpenUsersAsync(cancellationToken))
        {
            authors = await PublicDeckStore.ListFollowedAsync(connection, userId, cancellationToken);
        }

        var profiles = await accounts.GetProfilesAsync(authors, cancellationToken);
        return [.. authors.Where(profiles.ContainsKey).Select(id => new AuthorSummaryResponse(id, profiles[id].DisplayName))];
    }

    /// <summary>
    /// Adds a public deck to the favorites of a user, or removes it.
    /// </summary>
    /// <exception cref="DeckException">The deck is not a public deck.</exception>
    public async Task SetFavoriteAsync(Guid userId, Guid deckId, bool favorite, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenUsersAsync(cancellationToken);
        if (favorite && await PublicDeckStore.FindAsync(connection, deckId, cancellationToken) is null)
        {
            throw DeckException.NotFound();
        }

        await PublicDeckStore.SetFavoriteAsync(connection, userId, deckId, favorite, time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>
    /// Gets the social data of a deck: public or not, favorite of the user, counts and author.
    /// </summary>
    public async Task<PublicDeckSocialResponse> GetSocialAsync(Guid? userId, Guid deckId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenUsersAsync(cancellationToken);
        var row = await PublicDeckStore.FindAsync(connection, deckId, cancellationToken);
        if (row is null)
        {
            return new PublicDeckSocialResponse(false, false, 0, 0, null);
        }

        var favorite = userId is { } id && await PublicDeckStore.IsFavoriteAsync(connection, id, deckId, cancellationToken);
        var profiles = await accounts.GetProfilesAsync([row.OwnerId], cancellationToken);
        return new PublicDeckSocialResponse(true, favorite, row.FavoriteCount, row.CopyCount, Author(row.OwnerId, profiles));
    }

    /// <summary>
    /// Gets the profile of an author.
    /// </summary>
    /// <exception cref="DeckException">The account does not exist.</exception>
    public async Task<AuthorResponse> GetAuthorAsync(Guid? userId, Guid authorId, CancellationToken cancellationToken)
    {
        var profiles = await accounts.GetProfilesAsync([authorId], cancellationToken);
        if (!profiles.TryGetValue(authorId, out var profile))
        {
            throw AuthorNotFound();
        }

        await using var connection = await store.OpenUsersAsync(cancellationToken);
        var (decks, followers) = await PublicDeckStore.CountAuthorAsync(connection, authorId, cancellationToken);
        var following = userId is { } id && await PublicDeckStore.IsFollowingAsync(connection, id, authorId, cancellationToken);
        return new AuthorResponse(authorId, profile.DisplayName, decks, followers, following);
    }

    /// <summary>
    /// Follows an author, or stops to follow the author.
    /// </summary>
    /// <exception cref="DeckException">The account does not exist, or the user is the author.</exception>
    public async Task SetFollowAsync(Guid userId, Guid authorId, bool follow, CancellationToken cancellationToken)
    {
        if (follow && userId == authorId)
        {
            throw DeckException.Validation(new FieldError("authorId", FieldErrorCodes.NotAllowed, "You cannot follow yourself."));
        }

        if (follow && !(await accounts.GetProfilesAsync([authorId], cancellationToken)).ContainsKey(authorId))
        {
            throw AuthorNotFound();
        }

        await using var connection = await store.OpenUsersAsync(cancellationToken);
        await PublicDeckStore.SetFollowAsync(connection, userId, authorId, follow, time.GetUtcNow().UtcDateTime, cancellationToken);
    }

    private async Task<IReadOnlyList<PublicDeckResponse>> ToResponsesAsync(IReadOnlyList<PublicDeckRow> rows, CancellationToken cancellationToken)
    {
        var profiles = await accounts.GetProfilesAsync([.. rows.Select(row => row.OwnerId).Distinct()], cancellationToken);
        var cards = await catalog.GetCardsAsync([.. rows.SelectMany(row => row.CommanderIds).Distinct()], cancellationToken);
        var printings = await catalog.GetPrintingsAsync([.. cards.Values.Select(card => card.DefaultPrintingId).OfType<Guid>().Distinct()], cancellationToken);
        return [.. rows.Select(row => new PublicDeckResponse(
            row.DeckId,
            row.Name,
            row.Format,
            row.ColorIdentity,
            [.. row.CommanderIds.Where(cards.ContainsKey).Select(id => new PublicDeckCommanderResponse(
                id,
                cards[id].Name,
                cards[id].DefaultPrintingId is { } printingId && printings.TryGetValue(printingId, out var printing) ? printing.ImageUri : null))],
            Author(row.OwnerId, profiles),
            row.CardCount,
            row.CopyCount,
            row.FavoriteCount,
            new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc))))];
    }

    private static AuthorSummaryResponse Author(Guid ownerId, IReadOnlyDictionary<Guid, AccountProfile> profiles) =>
        new(ownerId, profiles.TryGetValue(ownerId, out var profile) ? profile.DisplayName : string.Empty);

    private static DeckException AuthorNotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The author does not exist.");
}
