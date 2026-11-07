using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.Api.Conventions;

/// <summary>
/// Applies the conventions of the public API: JSON settings and error responses.
/// See docs/architecture/api-conventions.md.
/// </summary>
internal static class ApiConventions
{
    /// <summary>
    /// Adds the JSON settings and the problem details settings of the public API.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddApiConventions(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            if (!problem.Extensions.ContainsKey(ApiProblems.CodeMember))
            {
                var (code, message) = DefaultError(problem.Status ?? StatusCodes.Status500InternalServerError);
                problem.Extensions[ApiProblems.CodeMember] = code;
                problem.Detail ??= message;
            }

            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
            problem.Extensions.Remove("requestId");
        });

        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    /// <summary>
    /// Gets the error code and the message of a status code, for the errors that an endpoint did not describe.
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <returns>The code and the message.</returns>
    public static (string Code, string Message) DefaultError(int status) => status switch
    {
        StatusCodes.Status400BadRequest => (ErrorCodes.BadRequest, "The request is not valid. Check the syntax, the JSON body and the parameters."),
        StatusCodes.Status401Unauthorized => (ErrorCodes.AuthenticationRequired, "The request needs authentication. Send an access token or an API key."),
        StatusCodes.Status403Forbidden => (ErrorCodes.Forbidden, "You cannot do this operation."),
        StatusCodes.Status404NotFound => (ErrorCodes.NotFound, "The resource does not exist. Check the URL."),
        StatusCodes.Status405MethodNotAllowed => (ErrorCodes.MethodNotAllowed, "The resource does not accept this HTTP method."),
        StatusCodes.Status409Conflict => (ErrorCodes.Conflict, "The request conflicts with the current state of the resource."),
        StatusCodes.Status429TooManyRequests => (ErrorCodes.RateLimited, "The client sent too many requests. Wait, then try again."),
        StatusCodes.Status503ServiceUnavailable => (ErrorCodes.ServiceUnavailable, "A service that the server needs is not available. Try again later."),
        _ => (ErrorCodes.InternalError, "The server could not complete the request. Give the trace ID to the administrator of the instance."),
    };
}

/// <summary>
/// Converts the exceptions of the endpoints into problem details with a stable error code.
/// </summary>
/// <param name="problems">The problem details service.</param>
/// <param name="logger">The logger. The handler logs each unexpected exception, because the framework does not log a handled exception.</param>
internal sealed partial class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, message) = exception switch
        {
            OwnerReadOnlyException => (
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.OwnerReadOnly,
                "Your data moves to another database, so it is read-only for a short time. Try again in one minute."),
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                ApiConventions.DefaultError(badRequest.StatusCode).Code,
                ApiConventions.DefaultError(badRequest.StatusCode).Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                ErrorCodes.InternalError,
                ApiConventions.DefaultError(StatusCodes.Status500InternalServerError).Message),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            LogUnexpectedException(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "60";
        }

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Detail = message,
                Extensions = { [ApiProblems.CodeMember] = code },
            },
        });
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Error, Message = "The request {Method} {Path} failed with an unexpected exception.")]
    private static partial void LogUnexpectedException(ILogger logger, Exception exception, string method, PathString path);
}
