using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Cards;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Analysis;
using Reliquary.Decks.Api;

namespace Reliquary.Decks.Versions;

/// <summary>One list of a comparison: the working list of a deck or of a variant, or one of its versions.</summary>
/// <param name="DeckId">The deck or the variant.</param>
/// <param name="VersionId">A version of the deck, or <see langword="null"/> for the working list.</param>
public sealed record ListReference(Guid? DeckId, Guid? VersionId);

/// <summary>The body of a comparison.</summary>
/// <param name="Left">The first list.</param>
/// <param name="Right">The second list. The changes go from the first list to the second list.</param>
public sealed record CompareListsRequest(ListReference? Left, ListReference? Right);

/// <summary>The analysis numbers of one list, for the effect of the changes.</summary>
/// <param name="CardCount">The number of cards in the commander, companion and main zones.</param>
/// <param name="LandCount">The number of lands.</param>
/// <param name="AverageManaValue">The average mana value of the spells.</param>
/// <param name="ManaCurve">The mana curve.</param>
/// <param name="Types">The card types.</param>
/// <param name="Categories">The categories.</param>
public sealed record ListSummaryResponse(int CardCount, int LandCount, decimal AverageManaValue, IReadOnlyList<CurvePoint> ManaCurve, IReadOnlyList<CountEntry> Types, IReadOnlyList<CountEntry> Categories);

/// <summary>The comparison of two lists.</summary>
/// <param name="Changes">The differences from the first list to the second list.</param>
/// <param name="Left">The numbers of the first list.</param>
/// <param name="Right">The numbers of the second list.</param>
public sealed record CompareListsResponse(ListChangesResponse Changes, ListSummaryResponse Left, ListSummaryResponse Right);

/// <summary>
/// The comparison of any two lists: deck versions, variants, working lists or different decks.
/// </summary>
internal static class CompareEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/lists/compare", CompareAsync)
            .WithTags("Deck versions")
            .AddEndpointFilter<DeckExceptionFilter>()
            .WithName("CompareLists")
            .WithSummary("Compares two lists that the user can see: working lists or versions of decks and variants. It gives the added and removed cards, the printing changes, the note changes and the effect on the curve, the types and the categories.")
            .RequireScope(Scopes.DecksRead);
    }

    private static async Task<Results<Ok<CompareListsResponse>, ProblemHttpResult>> CompareAsync(
        CompareListsRequest request, ClaimsPrincipal user, DeckVersionService versions, DeckService decks, ICardCatalog catalog, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (request.Left?.DeckId is null)
        {
            errors.Add(new FieldError("left.deckId", FieldErrorCodes.Required, "Give the deck of the first list."));
        }

        if (request.Right?.DeckId is null)
        {
            errors.Add(new FieldError("right.deckId", FieldErrorCodes.Required, "Give the deck of the second list."));
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var viewer = user.FindAccountId();
        var (leftFormat, left) = await LinesAsync(viewer, request.Left!, versions, decks, cancellationToken);
        var (_, right) = await LinesAsync(viewer, request.Right!, versions, decks, cancellationToken);
        var comparison = ListComparer.Compare(left.Select(line => line.Compared), right.Select(line => line.Compared));
        var cards = await catalog.GetCardsAsync([.. left.Concat(right).Select(line => line.Compared.OracleId).Distinct()], cancellationToken);
        return TypedResults.Ok(new CompareListsResponse(
            await VersionEndpoints.ToResponseAsync(comparison, catalog, cancellationToken),
            Summary(leftFormat, left, cards),
            Summary(leftFormat, right, cards)));
    }

    private static async Task<(string Format, IReadOnlyList<(ComparedLine Compared, IReadOnlyList<string> Categories)> Lines)> LinesAsync(
        Guid? viewerId, ListReference reference, DeckVersionService versions, DeckService decks, CancellationToken cancellationToken)
    {
        if (reference.VersionId is { } versionId)
        {
            var (_, lines, deck) = await versions.GetAsync(viewerId, reference.DeckId!.Value, versionId, cancellationToken);
            return (deck.Format, [.. lines.Select(line => (DeckVersionService.ToCompared(line), (IReadOnlyList<string>)line.Categories))]);
        }

        var view = await decks.GetAsync(viewerId, reference.DeckId!.Value, cancellationToken);
        return (view.Deck.Format, [.. view.Lines.Select(line => (new ComparedLine(line.OracleId, line.Zone, line.PrintingId, line.Finish, line.Quantity, line.Note), (IReadOnlyList<string>)line.Categories))]);
    }

    private static ListSummaryResponse Summary(string format, IReadOnlyList<(ComparedLine Compared, IReadOnlyList<string> Categories)> lines, IReadOnlyDictionary<Guid, CardFacts> cards)
    {
        var analysis = DeckAnalyzer.Analyze(format, lines.Select(line => new AnalysisLine(line.Compared.Zone, line.Compared.OracleId, line.Compared.Quantity, line.Categories)), cards);
        return new ListSummaryResponse(analysis.CardCount, analysis.LandCount, analysis.AverageManaValue, analysis.ManaCurve, analysis.Types, analysis.Categories);
    }
}
