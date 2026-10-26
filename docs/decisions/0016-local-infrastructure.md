# 0016. Local infrastructure: Docker Compose and just

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The specification asks for one command that creates, updates, resets and destroys a local instance.
It proposes Docker Compose or .NET Aspire.
Several local instances must run at the same time, with a project prefix, to test a plugin or a migration.
Front-end developers and plugin authors also need a local instance.

## Decision

- Docker Compose describes the local services. At the start, the services are PostgreSQL and Redis.
- `just` gives the commands: `just up`, `just stop`, `just reset`, `just down`, and the commands to build, test and check the code.
- The instance name (`RELIQUARY_INSTANCE`) is the prefix of the Compose project. The ports come from the `.env` file.
- .NET Aspire is not chosen.

## Consequences

- The commands for the infrastructure need Docker and `just` only. They do not need the .NET SDK.
- Issue [#10](https://github.com/nicolas-goyon/reliquary/issues/10) adds the API, the worker and the web app to the Compose file, so that `just up` creates a full instance.
- Aspire gives a dashboard for .NET services. OpenTelemetry and an optional Grafana service give the same information ([#12](https://github.com/nicolas-goyon/reliquary/issues/12)).
