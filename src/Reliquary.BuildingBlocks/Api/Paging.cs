using System.Buffers.Text;
using System.Text.Json;

namespace Reliquary.BuildingBlocks.Api;

/// <summary>
/// One page of a list, as the public API returns it.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <param name="Items">The items of the page.</param>
/// <param name="NextCursor">The cursor of the next page, or <see langword="null"/> on the last page.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// The paging parameters of a list request: <c>limit</c> and <c>cursor</c>.
/// </summary>
/// <param name="Limit">The maximum number of items in the page.</param>
/// <param name="Cursor">The cursor from the previous page, or <see langword="null"/> for the first page.</param>
public readonly record struct PageRequest(int Limit, string? Cursor)
{
    /// <summary>The number of items when the request gives no limit.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The maximum value of the limit.</summary>
    public const int MaxLimit = 100;

    /// <summary>
    /// Creates the paging parameters from the query parameters of a request.
    /// </summary>
    /// <param name="limit">The <c>limit</c> query parameter.</param>
    /// <param name="cursor">The <c>cursor</c> query parameter.</param>
    /// <param name="request">The paging parameters, when they are valid.</param>
    /// <param name="error">The field error, when they are not valid.</param>
    /// <returns><see langword="true"/> when the parameters are valid.</returns>
    public static bool TryCreate(int? limit, string? cursor, out PageRequest request, out FieldError? error)
    {
        var value = limit ?? DefaultLimit;
        if (value is < 1 or > MaxLimit)
        {
            request = default;
            error = new FieldError("limit", FieldErrorCodes.OutOfRange, $"The limit must be from 1 to {MaxLimit}.");
            return false;
        }

        request = new PageRequest(value, string.IsNullOrEmpty(cursor) ? null : cursor);
        error = null;
        return true;
    }
}

/// <summary>
/// Encodes and decodes the opaque cursors of the lists.
/// </summary>
/// <remarks>
/// A cursor is the base64url form of a JSON value, for example the sort key of the last item of a page.
/// Clients must not read or change a cursor.
/// </remarks>
public static class Cursors
{
    /// <summary>
    /// Encodes a position into a cursor.
    /// </summary>
    /// <typeparam name="T">The type of the position.</typeparam>
    /// <param name="position">The position, for example the sort key of the last item.</param>
    /// <returns>The cursor.</returns>
    public static string Encode<T>(T position) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(position));

    /// <summary>
    /// Decodes a cursor into a position.
    /// </summary>
    /// <typeparam name="T">The type of the position.</typeparam>
    /// <param name="cursor">The cursor.</param>
    /// <param name="position">The position, when the cursor is valid.</param>
    /// <returns><see langword="true"/> when the cursor is valid.</returns>
    public static bool TryDecode<T>(string cursor, out T? position)
    {
        try
        {
            position = JsonSerializer.Deserialize<T>(Base64Url.DecodeFromChars(cursor));
            return position is not null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException)
        {
            position = default;
            return false;
        }
    }

    /// <summary>
    /// Creates the field error of a cursor that is not valid.
    /// </summary>
    /// <returns>The field error.</returns>
    public static FieldError InvalidCursor() =>
        new("cursor", FieldErrorCodes.InvalidFormat, "The cursor is not valid. Use the cursor of the previous page without a change.");
}
