using Reliquary.Api;
using Reliquary.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");
builder.Services.AddHealthChecks();
builder.Services.AddReliquaryCore(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapHealthChecks("/health/live");

var api = app.MapGroup("/api/v1");

api.MapGet("/status", ApiStatus.Create)
    .WithName("GetStatus")
    .WithSummary("Gets the name, the version and the core components of the API.");

api.MapReliquaryEndpoints();

await app.RunAsync();

/// <summary>
/// The entry point of the API host. The tests use this type to start the host.
/// </summary>
public partial class Program;
