using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.Accounts.Auth;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.PersonalData;

/// <summary>An export of the data of the account.</summary>
/// <param name="Id">The ID of the export.</param>
/// <param name="Status"><c>pending</c>, <c>ready</c> or <c>failed</c>.</param>
/// <param name="RequestedAt">The time of the request.</param>
/// <param name="CompletedAt">The time when the archive was ready, or <see langword="null"/>.</param>
/// <param name="ExpiresAt">The time when the API deletes the archive, or <see langword="null"/>.</param>
/// <param name="SizeBytes">The size of the archive in bytes, or <see langword="null"/>.</param>
/// <param name="DownloadPath">The path of the archive in the API, when the archive is ready.</param>
public sealed record DataExportResponse(Guid Id, string Status, DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, DateTimeOffset? ExpiresAt, long? SizeBytes, string? DownloadPath);

/// <summary>The deletion request of the account.</summary>
/// <param name="RequestedAt">The time of the request, or <see langword="null"/> when the account has no deletion request.</param>
/// <param name="DeleteAfter">The time after which the worker deletes the account, or <see langword="null"/>.</param>
public sealed record DeletionResponse(DateTimeOffset? RequestedAt, DateTimeOffset? DeleteAfter);

/// <summary>The body of a deletion request.</summary>
/// <param name="Password">The password of the account. An account with no password does not need it.</param>
public sealed record DeletionRequest(string? Password);

/// <summary>
/// The endpoints of the export and of the deletion of the account. See docs/decisions/0032-personal-data.md.
/// </summary>
internal static class PersonalDataEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Account").RequireAuthorization(AuthPolicies.User).RequireAuthorization(AuthPolicies.Session);

        me.MapGet("/exports", ListExportsAsync)
            .WithName("ListDataExports")
            .WithSummary("Lists the recent exports of the data of the account.");

        me.MapPost("/exports", RequestExportAsync)
            .WithName("RequestDataExport")
            .WithSummary("Asks for an archive of all the data of the account. The worker makes the archive, and the archive stays available for 7 days.");

        me.MapGet("/exports/{exportId:guid}/archive", DownloadAsync)
            .WithName("DownloadDataExport")
            .WithSummary("Downloads the ZIP archive of an export.");

        me.MapGet("/deletion", GetDeletionAsync)
            .WithName("GetAccountDeletion")
            .WithSummary("Gets the deletion request of the account.");

        me.MapPost("/deletion", RequestDeletionAsync)
            .WithName("RequestAccountDeletion")
            .WithSummary("Asks for the deletion of the account. The worker deletes the account and all its data after 7 days, when the user does not cancel the request.")
            .RequireRateLimiting(AuthEndpoints.RateLimiterPolicy);

        me.MapDelete("/deletion", CancelDeletionAsync)
            .WithName("CancelAccountDeletion")
            .WithSummary("Cancels the deletion request of the account.");
    }

    private static async Task<Ok<IReadOnlyList<DataExportResponse>>> ListExportsAsync(ClaimsPrincipal user, PersonalDataService service, CancellationToken cancellationToken)
    {
        var rows = await service.ListExportsAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DataExportResponse>>([.. rows.Select(ToResponse)]);
    }

    private static async Task<Accepted<DataExportResponse>> RequestExportAsync(ClaimsPrincipal user, PersonalDataService service, CancellationToken cancellationToken)
    {
        var row = await service.RequestExportAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Accepted($"{ApiRoutes.Base}/me/exports", ToResponse(row));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadAsync(Guid exportId, ClaimsPrincipal user, PersonalDataService service, CancellationToken cancellationToken)
    {
        var archive = await service.GetArchiveAsync(user.GetAccountId(), exportId, cancellationToken);
        return archive is null
            ? ApiProblems.Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The archive does not exist, it is not ready, or it expired. Ask for a new export.")
            : TypedResults.File(archive, "application/zip", $"reliquary-export-{exportId:N}.zip");
    }

    private static async Task<Ok<DeletionResponse>> GetDeletionAsync(ClaimsPrincipal user, PersonalDataService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToDeletion(await service.GetDeletionRequestAsync(user.GetAccountId(), cancellationToken)));

    private static async Task<Results<Ok<DeletionResponse>, ProblemHttpResult>> RequestDeletionAsync(
        DeletionRequest request, ClaimsPrincipal user, AccountService accounts, PersonalDataService service, CancellationToken cancellationToken)
    {
        if (!await accounts.CheckPasswordAsync(user.GetAccountId(), request.Password, cancellationToken))
        {
            return ApiProblems.Problem(StatusCodes.Status400BadRequest, AccountErrorCodes.InvalidCredentials, "The password is not correct.");
        }

        return TypedResults.Ok(ToDeletion(await service.SetDeletionRequestAsync(user.GetAccountId(), true, cancellationToken)));
    }

    private static async Task<NoContent> CancelDeletionAsync(ClaimsPrincipal user, PersonalDataService service, CancellationToken cancellationToken)
    {
        await service.SetDeletionRequestAsync(user.GetAccountId(), false, cancellationToken);
        return TypedResults.NoContent();
    }

    private static DataExportResponse ToResponse(DataExportRow row) => new(
        row.Id,
        row.Status,
        Utc(row.RequestedAt),
        row.CompletedAt is { } completed ? Utc(completed) : null,
        row.ExpiresAt is { } expires ? Utc(expires) : null,
        row.SizeBytes,
        row.Status == "ready" ? $"{ApiRoutes.Base}/me/exports/{row.Id}/archive" : null);

    private static DeletionResponse ToDeletion(DateTime? requestedAt) =>
        requestedAt is { } value ? new(Utc(value), Utc(value + PersonalDataService.DeletionDelay)) : new(null, null);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
