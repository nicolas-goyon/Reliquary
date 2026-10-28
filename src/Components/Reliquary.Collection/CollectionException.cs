using Microsoft.AspNetCore.Http;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Collection;

namespace Reliquary.Collection;

/// <summary>
/// An error of a collection operation, with the status and the stable code of the API response.
/// </summary>
internal sealed class CollectionException(int status, string code, string message, IReadOnlyList<FieldError>? errors = null) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public IReadOnlyList<FieldError> Errors { get; } = errors ?? [];

    public static CollectionException LocationNotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The location does not exist.");

    public static CollectionException EntryNotFound() =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The entry does not exist.");

    public static CollectionException NotFound(string message) =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);

    public static CollectionException Validation(params FieldError[] errors) =>
        new(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "One or more fields of the request are not valid. Correct the fields in the errors list.", errors);

    public static CollectionException Locked(string name) =>
        new(StatusCodes.Status409Conflict, CollectionErrorCodes.LocationLocked, $"The location \"{name}\" is locked. Unlock the location, then try again.");

    public static CollectionException DeckLocation() =>
        new(StatusCodes.Status409Conflict, CollectionErrorCodes.DeckLocation, "The location belongs to an assembled deck. Change the deck to change the location.");

    public static CollectionException NotEnoughCards(string cardName, int available) =>
        new(StatusCodes.Status409Conflict, CollectionErrorCodes.NotEnoughCards, $"The origin has {available} copies of \"{cardName}\". Give a lower quantity.");
}

/// <summary>
/// Converts a <see cref="CollectionException"/> and a <see cref="LocationLockedException"/> into a problem response.
/// </summary>
internal sealed class CollectionExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (CollectionException exception)
        {
            var extensions = exception.Errors.Count > 0
                ? new Dictionary<string, object?> { [ApiProblems.ErrorsMember] = exception.Errors }
                : null;
            return ApiProblems.Problem(exception.Status, exception.Code, exception.Message, extensions);
        }
        catch (LocationLockedException exception)
        {
            return ApiProblems.Conflict(CollectionErrorCodes.LocationLocked, exception.Message);
        }
    }
}
