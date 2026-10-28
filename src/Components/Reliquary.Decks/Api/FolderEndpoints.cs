using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.Decks.Data;

namespace Reliquary.Decks.Api;

/// <summary>
/// The endpoints of the folders of decks.
/// </summary>
internal static class FolderEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var folders = api.MapGroup("/deck-folders").WithTags("Decks").AddEndpointFilter<DeckExceptionFilter>().RequireAuthorization(AuthPolicies.User);

        folders.MapGet(string.Empty, ListAsync)
            .WithName("ListDeckFolders")
            .WithSummary("Lists all the folders of the user. A folder has a parent folder, or no parent folder at the top.")
            .RequireScope(Scopes.DecksRead);

        folders.MapPost(string.Empty, CreateAsync)
            .WithName("CreateDeckFolder")
            .WithSummary("Creates a folder, at the top or in another folder.")
            .RequireScope(Scopes.DecksWrite);

        folders.MapPatch("/{folderId:guid}", UpdateAsync)
            .WithName("UpdateDeckFolder")
            .WithSummary("Renames a folder or moves it into another folder.")
            .RequireScope(Scopes.DecksWrite);

        folders.MapDelete("/{folderId:guid}", DeleteAsync)
            .WithName("DeleteDeckFolder")
            .WithSummary("Deletes a folder and its subfolders. Their decks stay, with no folder.")
            .RequireScope(Scopes.DecksWrite);
    }

    private static async Task<Ok<IReadOnlyList<FolderResponse>>> ListAsync(ClaimsPrincipal user, FolderService folders, CancellationToken cancellationToken)
    {
        var rows = await folders.ListAsync(user.GetAccountId(), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<FolderResponse>>([.. rows.Select(ToResponse)]);
    }

    private static async Task<Results<Created<FolderResponse>, ProblemHttpResult>> CreateAsync(CreateFolderRequest request, ClaimsPrincipal user, FolderService folders, CancellationToken cancellationToken)
    {
        if (ValidateName(request.Name, required: true) is { } error)
        {
            return ApiProblems.Validation(error);
        }

        var folder = await folders.CreateAsync(user.GetAccountId(), request.Name!, request.ParentId, cancellationToken);
        return TypedResults.Created($"{ApiRoutes.Base}/deck-folders/{folder.Id}", ToResponse(folder));
    }

    private static async Task<Results<Ok<FolderResponse>, ProblemHttpResult>> UpdateAsync(Guid folderId, UpdateFolderRequest request, ClaimsPrincipal user, FolderService folders, CancellationToken cancellationToken)
    {
        if (ValidateName(request.Name, required: false) is { } error)
        {
            return ApiProblems.Validation(error);
        }

        var folder = await folders.UpdateAsync(user.GetAccountId(), folderId, request.Name, request.ParentId, request.MoveToTop == true, cancellationToken);
        return TypedResults.Ok(ToResponse(folder));
    }

    private static async Task<NoContent> DeleteAsync(Guid folderId, ClaimsPrincipal user, FolderService folders, CancellationToken cancellationToken)
    {
        await folders.DeleteAsync(user.GetAccountId(), folderId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static FieldError? ValidateName(string? name, bool required)
    {
        if (name is null)
        {
            return required ? new FieldError("name", FieldErrorCodes.Required, "The name of the folder is necessary.") : null;
        }

        var trimmed = name.Trim();
        return trimmed.Length switch
        {
            0 => new FieldError("name", FieldErrorCodes.TooShort, "The name of the folder must contain a character that is not a space."),
            > DeckRules.MaxLabelLength => new FieldError("name", FieldErrorCodes.TooLong, $"The name of the folder must have {DeckRules.MaxLabelLength} characters or fewer."),
            _ => null,
        };
    }

    private static FolderResponse ToResponse(FolderRow row) => new(row.Id, row.ParentId, row.Name, row.DeckCount);
}
