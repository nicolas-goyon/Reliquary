using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Reliquary.Api;
using Reliquary.Api.Conventions;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;
using Reliquary.BuildingBlocks.Versioning;
using Reliquary.Hosting;
using Reliquary.Platform.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiConventions();
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Reliquary public API";
    document.Info.Version = "v1";
    document.Info.Description = "The public API of Reliquary. The web interface, the plugins and the third-party clients use it. See docs/architecture/api-conventions.md.";
    return Task.CompletedTask;
}));
builder.Services.AddOpenApi(ApiRoutes.InternalDocument, options =>
{
    options.ShouldInclude = description => description.GroupName == ApiRoutes.InternalDocument;
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Reliquary internal API";
        document.Info.Version = CoreApi.Version.ToString();
        document.Info.Description = "The internal API of Reliquary for the modules. Only service accounts can use it. The version is the version of the core API. See docs/architecture/internal-api.md.";
        return Task.CompletedTask;
    });
});
builder.Services.AddReliquaryCore(builder.Configuration);
builder.Services.AddReliquaryApi(builder.Configuration);

builder.AddReliquaryTelemetry("reliquary-api")
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(options =>
        options.Filter = context => !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/metrics")))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapHealthEndpoints();
app.MapPrometheusScrapingEndpoint("/metrics");

var api = app.MapGroup(ApiRoutes.Base);

api.MapGet("/status", ApiStatus.Create)
    .WithName("GetStatus")
    .WithSummary("Gets the name, the version and the core components of the API.");

api.MapReliquaryEndpoints();

// The internal API for the modules. It has its own OpenAPI document, and only service accounts can use it.
app.MapGroup(ApiRoutes.InternalBase)
    .WithGroupName(ApiRoutes.InternalDocument)
    .RequireAuthorization(AuthPolicies.ServiceAccount)
    .MapReliquaryInternalEndpoints();

await app.RunAsync();

/// <summary>
/// The entry point of the API host. The tests use this type to start the host.
/// </summary>
public partial class Program;
