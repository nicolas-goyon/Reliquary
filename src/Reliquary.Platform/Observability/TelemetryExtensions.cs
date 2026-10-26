using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Reliquary.BuildingBlocks;

namespace Reliquary.Platform.Observability;

/// <summary>
/// Adds the traces, the metrics and the logs of OpenTelemetry to a host.
/// </summary>
/// <remarks>
/// When the variable <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> has a value, the host sends the three signals with OTLP,
/// for example to the Grafana service of the local instance. See docs/development/observability.md.
/// </remarks>
public static class TelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry to a host, with the instrumentation that all hosts share: PostgreSQL, HTTP clients, the .NET runtime and Reliquary.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="serviceName">The name of the service, for example <c>reliquary-api</c>.</param>
    /// <returns>The OpenTelemetry builder, for the instrumentation of the host.</returns>
    public static IOpenTelemetryBuilder AddReliquaryTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version.Split('+')[0], serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.Name)
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.Name)
                .AddMeter("Npgsql")
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        return telemetry;
    }
}
