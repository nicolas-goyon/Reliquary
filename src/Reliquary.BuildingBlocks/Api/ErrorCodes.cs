namespace Reliquary.BuildingBlocks.Api;

/// <summary>
/// The stable error codes of the public API. The web interface translates each code into the language of the user.
/// </summary>
/// <remarks>
/// A code never changes after its release. Add a new code for a new error.
/// docs/architecture/api-conventions.md lists all the codes.
/// </remarks>
public static class ErrorCodes
{
    /// <summary>The request is not valid: the syntax, the JSON or a parameter type is not correct.</summary>
    public const string BadRequest = "bad_request";

    /// <summary>One or more fields of the request are not valid. The <c>errors</c> member gives each field.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>The request needs an authenticated user.</summary>
    public const string AuthenticationRequired = "authentication_required";

    /// <summary>The user cannot do this operation.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>The resource does not exist, or the user cannot see it.</summary>
    public const string NotFound = "not_found";

    /// <summary>The resource does not accept this HTTP method.</summary>
    public const string MethodNotAllowed = "method_not_allowed";

    /// <summary>The request conflicts with the current state of the resource.</summary>
    public const string Conflict = "conflict";

    /// <summary>The API key or the account used all its requests of the day.</summary>
    public const string QuotaExceeded = "quota_exceeded";

    /// <summary>The client sent too many requests in a short time.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>The data of the user moves to another database. The data is read-only for a short time.</summary>
    public const string OwnerReadOnly = "owner_read_only";

    /// <summary>The server could not complete the request because of an error on the server.</summary>
    public const string InternalError = "internal_error";

    /// <summary>A service that the server needs is not available.</summary>
    public const string ServiceUnavailable = "service_unavailable";
}
