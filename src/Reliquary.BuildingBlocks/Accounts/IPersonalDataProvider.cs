namespace Reliquary.BuildingBlocks.Accounts;

/// <summary>
/// Gives the personal data of a core component for the export of an account, and deletes it for the deletion of an account.
/// Each core component that stores data of an owner implements it. See docs/decisions/0032-personal-data.md.
/// </summary>
public interface IPersonalDataProvider
{
    /// <summary>
    /// Gets the name of the folder of the component in the export archive, for example <c>decks</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the order of the deletion. A provider with a lower order deletes its data first.
    /// </summary>
    int DeletionOrder => 0;

    /// <summary>
    /// Writes all the data of an owner to the export archive, in open formats such as JSON and text.
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="archive">The archive. The paths are relative to the folder of the component.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the archive contains the data.</returns>
    Task ExportAsync(Guid ownerId, IPersonalDataArchive archive, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes all the data of an owner. A second call gives the same result.
    /// </summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when no table of the component contains the owner.</returns>
    Task DeleteAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>
/// The archive of the export of an account.
/// </summary>
public interface IPersonalDataArchive
{
    /// <summary>
    /// Writes a value as an indented JSON file with camel case names.
    /// </summary>
    /// <param name="path">The path of the file in the folder of the component, for example <c>decks.json</c>.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the archive contains the file.</returns>
    Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a text file in UTF-8.
    /// </summary>
    /// <param name="path">The path of the file in the folder of the component, for example <c>decks/Atraxa.txt</c>.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the archive contains the file.</returns>
    Task WriteTextAsync(string path, string text, CancellationToken cancellationToken);
}
