# Observability

This guide tells how the API and the worker report their state and their activity.
Terms follow the [glossary](../glossary.md).

## Signals

The API and the worker use OpenTelemetry for the three signals:

| Signal | Content |
| --- | --- |
| Traces | One trace for each API request, with a span for each PostgreSQL command and each outgoing HTTP request |
| Metrics | HTTP requests, PostgreSQL connections and commands, .NET runtime, and the metrics of Reliquary |
| Logs | The logs of the hosts, with the trace ID of the request |

The core components use the trace source and the meter `Reliquary` in `Reliquary.BuildingBlocks.Telemetry`.

## Endpoints

| Endpoint | API | Worker | Meaning |
| --- | --- | --- | --- |
| `/health/live` | Yes | Yes | The process runs. This check uses no other service. |
| `/health/ready` | Yes | Yes | PostgreSQL and Redis answer. The status is 503 when a service does not answer. |
| `/metrics` | Yes | Yes | The metrics in the Prometheus text format |

The worker has no public endpoint. Its HTTP server serves only these three endpoints.
In development, the worker listens on <http://localhost:5081>.

Do not publish `/metrics` on the internet.
Block it in the reverse proxy, or serve it on an internal network only.

The traces do not include the requests to `/health` and `/metrics`.

## Send the signals to Grafana

The local infrastructure has an optional Grafana service, with Prometheus, Tempo, Loki and an OpenTelemetry collector.

1. Run `just observability`.
2. Set the variable `OTEL_EXPORTER_OTLP_ENDPOINT` to `http://localhost:4317` for the API and the worker.
3. Start the API and the worker.
4. Open Grafana on <http://localhost:3000>.

When `OTEL_EXPORTER_OTLP_ENDPOINT` has no value, the hosts send no signal with OTLP.
The `/metrics` endpoints stay available.

The standard OpenTelemetry variables also apply, for example `OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_RESOURCE_ATTRIBUTES`.

## Service names

| Host | Service name |
| --- | --- |
| API | `reliquary-api` |
| Worker | `reliquary-worker` |
