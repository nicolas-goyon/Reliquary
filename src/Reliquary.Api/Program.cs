using Reliquary.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");
builder.Services.AddHealthChecks();

foreach (var component in CoreComponents.All)
{
    component.AddServices(builder.Services, builder.Configuration);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapHealthChecks("/health/live");

var api = app.MapGroup("/api/v1");

api.MapGet("/status", ApiStatus.Create)
    .WithName("GetStatus")
    .WithSummary("Gets the name, the version and the core components of the API.");

foreach (var component in CoreComponents.All)
{
    component.MapEndpoints(api);
}

await app.RunAsync();
