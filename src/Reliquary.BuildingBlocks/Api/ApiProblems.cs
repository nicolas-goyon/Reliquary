using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Reliquary.BuildingBlocks.Api;

/// <summary>
/// Creates the error responses of the public API: problem details (RFC 9457) with a stable <c>code</c> member.
/// </summary>
/// <remarks>
/// Write each message in the strict mode of the writing rules: tell what happened, then tell what the client can do.
/// </remarks>
public static class ApiProblems
{
    /// <summary>The name of the member that contains the error code.</summary>
    public const string CodeMember = "code";

    /// <summary>The name of the member that contains the field errors of a validation error.</summary>
    public const string ErrorsMember = "errors";

    /// <summary>
    /// Creates a problem response.
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="code">The stable error code. See <see cref="ErrorCodes"/>.</param>
    /// <param name="message">The English message for developers.</param>
    /// <param name="extensions">Other members of the response.</param>
    /// <returns>The response.</returns>
    public static ProblemHttpResult Problem(int status, string code, string message, IDictionary<string, object?>? extensions = null)
    {
        var members = extensions is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(extensions);
        members[CodeMember] = code;
        return TypedResults.Problem(detail: message, statusCode: status, extensions: members);
    }

    /// <summary>
    /// Creates a validation error: HTTP 400 with the code <see cref="ErrorCodes.ValidationFailed"/> and the error of each field.
    /// </summary>
    /// <param name="errors">The field errors.</param>
    /// <returns>The response.</returns>
    public static ProblemHttpResult Validation(params IEnumerable<FieldError> errors) =>
        Problem(
            StatusCodes.Status400BadRequest,
            ErrorCodes.ValidationFailed,
            "One or more fields of the request are not valid. Correct the fields in the errors list.",
            new Dictionary<string, object?> { [ErrorsMember] = errors.ToList() });

    /// <summary>
    /// Creates a "not found" error: HTTP 404 with the code <see cref="ErrorCodes.NotFound"/>.
    /// </summary>
    /// <param name="message">The message, for example "The deck does not exist.".</param>
    /// <returns>The response.</returns>
    public static ProblemHttpResult NotFound(string message) =>
        Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);

    /// <summary>
    /// Creates a conflict error: HTTP 409.
    /// </summary>
    /// <param name="code">The specific error code, or <see cref="ErrorCodes.Conflict"/>.</param>
    /// <param name="message">The message.</param>
    /// <returns>The response.</returns>
    public static ProblemHttpResult Conflict(string code, string message) =>
        Problem(StatusCodes.Status409Conflict, code, message);
}

/// <summary>
/// The error of one field in a validation error.
/// </summary>
/// <param name="Field">The name of the field, in camel case, as in the request. Use a dot for a nested field, for example <c>card.name</c>.</param>
/// <param name="Code">The stable error code of the field, for example <c>required</c> or <c>too_long</c>.</param>
/// <param name="Message">The English message for developers.</param>
public sealed record FieldError(string Field, string Code, string Message);

/// <summary>
/// The common error codes of the fields.
/// </summary>
public static class FieldErrorCodes
{
    /// <summary>The field is necessary.</summary>
    public const string Required = "required";

    /// <summary>The value is too short.</summary>
    public const string TooShort = "too_short";

    /// <summary>The value is too long.</summary>
    public const string TooLong = "too_long";

    /// <summary>The value is outside the permitted range.</summary>
    public const string OutOfRange = "out_of_range";

    /// <summary>The value does not have the correct format.</summary>
    public const string InvalidFormat = "invalid_format";

    /// <summary>The value is not one of the permitted values.</summary>
    public const string NotAllowed = "not_allowed";
}
