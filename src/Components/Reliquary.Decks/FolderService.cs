using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.Decks.Data;

namespace Reliquary.Decks;

/// <summary>
/// The operations on the folders of decks. A folder can contain other folders.
/// </summary>
internal sealed class FolderService(DeckStore store, TimeProvider time)
{
    public async Task<IReadOnlyList<FolderRow>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Read, cancellationToken);
        return await DeckStore.ListFoldersAsync(connection, ownerId, cancellationToken);
    }

    /// <summary>
    /// Creates a folder.
    /// </summary>
    /// <exception cref="DeckException">The parent folder does not exist, or the folder is too deep.</exception>
    public async Task<FolderRow> CreateAsync(Guid ownerId, string name, Guid? parentId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        var folders = await DeckStore.ListFoldersAsync(connection, ownerId, cancellationToken);
        CheckParent(folders, null, parentId);
        var now = time.GetUtcNow().UtcDateTime;
        var folder = new FolderRow { Id = Ids.New(), ParentId = parentId, Name = name.Trim(), CreatedAt = now, UpdatedAt = now };
        await DeckStore.InsertFolderAsync(connection, ownerId, folder, cancellationToken);
        return folder;
    }

    /// <summary>
    /// Renames a folder or moves it into another folder.
    /// </summary>
    /// <exception cref="DeckException">The folder does not exist, or the move makes a cycle or a folder that is too deep.</exception>
    public async Task<FolderRow> UpdateAsync(Guid ownerId, Guid folderId, string? name, Guid? parentId, bool moveToTop, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var folders = await DeckStore.ListFoldersAsync(connection, ownerId, cancellationToken, transaction);
        var folder = folders.FirstOrDefault(row => row.Id == folderId) ?? throw FolderNotFound();
        if (moveToTop || parentId is not null)
        {
            var newParent = moveToTop ? null : parentId;
            CheckParent(folders, folder, newParent);
            folder.ParentId = newParent;
        }

        folder.Name = name?.Trim() ?? folder.Name;
        folder.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await DeckStore.UpdateFolderAsync(transaction, ownerId, folder, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return folder;
    }

    /// <summary>
    /// Deletes a folder and its subfolders. Their decks stay, with no folder.
    /// </summary>
    /// <exception cref="DeckException">The folder does not exist.</exception>
    public async Task DeleteAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(ownerId, ContentAccess.Write, cancellationToken);
        if (await DeckStore.DeleteFolderAsync(connection, ownerId, folderId, cancellationToken) == 0)
        {
            throw FolderNotFound();
        }
    }

    /// <summary>
    /// Checks a new parent of a folder: it exists, it is not the folder or one of its subfolders, and the result is not too deep.
    /// </summary>
    private static void CheckParent(IReadOnlyList<FolderRow> folders, FolderRow? folder, Guid? parentId)
    {
        if (parentId is null)
        {
            return;
        }

        var byId = folders.ToDictionary(row => row.Id);
        if (!byId.ContainsKey(parentId.Value))
        {
            throw DeckException.Validation(new FieldError("parentId", FieldErrorCodes.NotAllowed, "The parent folder does not exist."));
        }

        // The depth of the parent: the number of folders from the top to the parent.
        var depth = 0;
        for (Guid? current = parentId; current is { } id; current = byId[id].ParentId)
        {
            if (folder is not null && id == folder.Id)
            {
                throw DeckException.Validation(new FieldError("parentId", FieldErrorCodes.NotAllowed, "A folder cannot go into itself or into one of its subfolders."));
            }

            depth++;
        }

        if (depth + (folder is null ? 1 : Height(folders, folder.Id)) > DeckRules.MaxFolderDepth)
        {
            throw DeckException.Validation(new FieldError("parentId", FieldErrorCodes.NotAllowed, $"The folders can have {DeckRules.MaxFolderDepth} levels or fewer."));
        }
    }

    /// <summary>Gets the number of levels of a folder and its subfolders. A folder with no subfolder has the height 1.</summary>
    private static int Height(IReadOnlyList<FolderRow> folders, Guid folderId) =>
        1 + folders.Where(row => row.ParentId == folderId).Select(row => Height(folders, row.Id)).DefaultIfEmpty(0).Max();

    private static DeckException FolderNotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The folder does not exist.");
}
