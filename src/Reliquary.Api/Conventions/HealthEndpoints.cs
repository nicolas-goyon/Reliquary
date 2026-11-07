using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reliquary.Platform.Health;

namespace Reliquary.Api.Conventions;

/// <summary>
/// Maps the health endpoints of the API.
/// </summary>
internal static class HealthEndpoints
{
    /// <summary>
    /// Maps <c>/health/live</c> and <c>/health/ready</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>/health/live</c> answers when the process runs. It checks no other service.</item>
    /// <item><c>/health/ready</c> answers HTTP 200 when PostgreSQL and Redis answer, and HTTP 503 when a service does not answer.</item>
    /// </list>
    /// </remarks>
    /// <param name="app">The endpoint builder.</param>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthTags.Ready),
            ResponseWriter = WriteReportAsync,
        });
    }

    private static Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
    }
}
