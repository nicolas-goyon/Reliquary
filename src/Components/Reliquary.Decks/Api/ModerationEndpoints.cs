using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Moderation;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of the moderation: the reports of the users and the actions of the administrators.
/// </summary>
internal static class ModerationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/reports", ReportAsync)
            .WithTags("Moderation")
            .AddEndpointFilter<DeckExceptionFilter>()
            .RequireAuthorization(AuthPolicies.User)
            .WithName("ReportContent")
            .WithSummary("Reports a public deck or an author profile to the administrators. A user has one open report for each item, and 20 reports in 24 hours at most.");

        var moderation = api.MapGroup("/moderation").WithTags("Moderation").AddEndpointFilter<DeckExceptionFilter>().RequireAuthorization(AuthPolicies.Administrator);

        moderation.MapGet("/reports", ListReportsAsync)
            .WithName("ListReports")
            .WithSummary("Lists the reports with a status. The default status is open. The oldest open report comes first.");

        moderation.MapPost("/reports/{reportId:guid}/resolve", ResolveAsync)
            .WithName("ResolveReport")
            .WithSummary("Closes an open report as resolved.");

        moderation.MapPost("/reports/{reportId:guid}/dismiss", DismissAsync)
            .WithName("DismissReport")
            .WithSummary("Closes an open report as dismissed, with no action on the item.");

        moderation.MapGet("/hidden", ListHiddenAsync)
            .WithName("ListHiddenContent")
            .WithSummary("Lists the hidden decks and author profiles, the most recent first.");

        moderation.MapPut("/hidden/{itemType}/{itemId:guid}", HideAsync)
            .WithName("HideContent")
            .WithSummary("Hides a deck or an author profile. The search of public decks does not show it after the response, and its open reports become resolved.");

        moderation.MapDelete("/hidden/{itemType}/{itemId:guid}", ShowAsync)
            .WithName("ShowContent")
            .WithSummary("Shows a hidden deck or author profile again. The query parameter reason gives the reason for the moderation log.");

        moderation.MapGet("/log", ListLogAsync)
            .WithName("ListModerationLog")
            .WithSummary("Lists the actions of the administrators, the most recent first.");
    }

    private static async Task<Results<Created<ReportResponse>, ProblemHttpResult>> ReportAsync(CreateReportRequest request, ClaimsPrincipal user, ModerationService service, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrEmpty(request.ItemType))
        {
            errors.Add(new FieldError("itemType", FieldErrorCodes.Required, "Give the item type: deck or author."));
        }
        else if (!ModerationValues.ItemTypes.Contains(request.ItemType))
        {
            errors.Add(new FieldError("itemType", FieldErrorCodes.NotAllowed, "Use one of these values: deck, author."));
        }

        if (request.ItemId is null)
        {
            errors.Add(new FieldError("itemId", FieldErrorCodes.Required, "Give the ID of the deck or of the author."));
        }

        if (string.IsNullOrEmpty(request.Reason))
        {
            errors.Add(new FieldError("reason", FieldErrorCodes.Required, "Give the reason of the report."));
        }
        else if (!ModerationValues.Reasons.Contains(request.Reason))
        {
            errors.Add(new FieldError("reason", FieldErrorCodes.NotAllowed, $"Use one of these values: {string.Join(", ", ModerationValues.Reasons)}."));
        }

        var comment = request.Comment?.Trim() ?? string.Empty;
        if (CommentError("comment", comment) is { } commentError)
        {
            errors.Add(commentError);
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var report = await service.ReportAsync(user.GetAccountId(), request.ItemType!, request.ItemId!.Value, request.Reason!, comment, cancellationToken);
        return TypedResults.Created(
            $"{ApiRoutes.Base}/reports/{report.Id}",
            new ReportResponse(report.Id, report.ItemType, report.ItemId, report.Reason, report.Comment, report.Status, new DateTimeOffset(DateTime.SpecifyKind(report.CreatedAt, DateTimeKind.Utc))));
    }

    private static async Task<Results<Ok<Page<ModerationReportResponse>>, ProblemHttpResult>> ListReportsAsync(
        ModerationService service, string? status, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (status is not null && !ModerationValues.Statuses.Contains(status))
        {
            return ApiProblems.Validation(new FieldError("status", FieldErrorCodes.NotAllowed, $"Use one of these values: {string.Join(", ", ModerationValues.Statuses)}."));
        }

        if (!TryReadPage(limit, cursor, out var page, out var offset, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        return TypedResults.Ok(ToPage(await service.ListReportsAsync(status ?? "open", offset, page.Limit + 1, cancellationToken), page.Limit, offset));
    }

    private static Task<Results<NoContent, ProblemHttpResult>> ResolveAsync(Guid reportId, ModerationActionRequest? request, ClaimsPrincipal user, ModerationService service, CancellationToken cancellationToken) =>
        CloseAsync(reportId, "resolved", request, user, service, cancellationToken);

    private static Task<Results<NoContent, ProblemHttpResult>> DismissAsync(Guid reportId, ModerationActionRequest? request, ClaimsPrincipal user, ModerationService service, CancellationToken cancellationToken) =>
        CloseAsync(reportId, "dismissed", request, user, service, cancellationToken);

    private static async Task<Results<NoContent, ProblemHttpResult>> CloseAsync(Guid reportId, string status, ModerationActionRequest? request, ClaimsPrincipal user, ModerationService service, CancellationToken cancellationToken)
    {
        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (CommentError("reason", reason) is { } error)
        {
            return ApiProblems.Validation(error);
        }

        await service.CloseReportAsync(user.GetAccountId(), reportId, status, reason, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<Page<HiddenItemResponse>>, ProblemHttpResult>> ListHiddenAsync(ModerationService service, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (!TryReadPage(limit, cursor, out var page, out var offset, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        return TypedResults.Ok(ToPage(await service.ListHiddenAsync(offset, page.Limit + 1, cancellationToken), page.Limit, offset));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HideAsync(
        string itemType, Guid itemId, ModerationActionRequest? request, ClaimsPrincipal user, ModerationService service, CancellationToken cancellationToken)
    {
        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (ActionErrors(itemType, reason) is { Count: > 0 } errors)
        {
            return ApiProblems.Validation(errors);
        }

        await service.HideAsync(user.GetAccountId(), itemType, itemId, reason, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ShowAsync(
        string itemType, Guid itemId, string? reason, ClaimsPrincipal user, ModerationService service, CancellationToken cancellationToken)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (ActionErrors(itemType, reason) is { Count: > 0 } errors)
        {
            return ApiProblems.Validation(errors);
        }

        await service.ShowAsync(user.GetAccountId(), itemType, itemId, reason, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<Page<ModerationLogEntryResponse>>, ProblemHttpResult>> ListLogAsync(ModerationService service, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (!TryReadPage(limit, cursor, out var page, out var offset, out var error))
        {
            return ApiProblems.Validation(error!);
        }

        return TypedResults.Ok(ToPage(await service.ListLogAsync(offset, page.Limit + 1, cancellationToken), page.Limit, offset));
    }

    private static List<FieldError> ActionErrors(string itemType, string reason)
    {
        var errors = new List<FieldError>();
        if (!ModerationValues.ItemTypes.Contains(itemType))
        {
            errors.Add(new FieldError("itemType", FieldErrorCodes.NotAllowed, "Use one of these values: deck, author."));
        }

        if (CommentError("reason", reason) is { } error)
        {
            errors.Add(error);
        }

        return errors;
    }

    private static FieldError? CommentError(string field, string value) =>
        value.Length > ModerationValues.MaxCommentLength
            ? new FieldError(field, FieldErrorCodes.TooLong, $"The text must have {ModerationValues.MaxCommentLength} characters or fewer.")
            : null;

    private static bool TryReadPage(int? limit, string? cursor, out PageRequest page, out int offset, out FieldError? error)
    {
        offset = 0;
        if (!PageRequest.TryCreate(limit, cursor, out page, out error))
        {
            return false;
        }

        if (page.Cursor is { } value)
        {
            if (!Cursors.TryDecode<OffsetCursor>(value, out var position) || position!.Offset < 0)
            {
                error = Cursors.InvalidCursor();
                return false;
            }

            offset = position.Offset;
        }

        return true;
    }

    private static Page<T> ToPage<T>(IReadOnlyList<T> rows, int limit, int offset) =>
        new([.. rows.Take(limit)], rows.Count > limit ? Cursors.Encode(new OffsetCursor(offset + limit)) : null);

    private sealed record OffsetCursor(int Offset);
}
