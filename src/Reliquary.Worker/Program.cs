using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Reliquary.Hosting;
using Reliquary.Platform.Health;
using Reliquary.Platform.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReliquaryCore(builder.Configuration);
builder.Services.AddReliquaryWorker(builder.Configuration);
builder.AddReliquaryTelemetry("reliquary-worker")
    .WithMetrics(metrics => metrics.AddPrometheusExporter());

var app = builder.Build();

// The worker has no public endpoint. Its HTTP server serves the health endpoints and the metrics, for the operators only.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) });
app.MapPrometheusScrapingEndpoint("/metrics");

await app.RunAsync();
