# Technology stack

This file gives the technical requirements of Reliquary: the backend language, the performance targets, the scaling, the local development and the stack.
Terms follow the [glossary](../glossary.md).

## Summary

The backend uses C# and ASP.NET Core.
The front end and the plugins use TypeScript.
The database is PostgreSQL.
The API keeps no state, so that Kubernetes can add replicas of the API.
All data access uses a routing by user, so that an operator can move or split the database later.

## Why C# and ASP.NET Core

The specification asks if C# and ASP.NET Core are a good fit for the project.
The answer is yes:

- ASP.NET Core is one of the fastest web frameworks in public benchmarks.
- ASP.NET Core runs well in Linux containers.
- ASP.NET Core gives, as standard, the features that the project needs:
  - SignalR for the real time of online games
  - built-in rate limiting for the quotas
  - EF Core or raw SQL
  - OpenTelemetry

Go would use less memory.
But C# will be more productive for a rich domain with decks, deck versions and the collection.
Also, the maintainer knows C# better than Go.
[ADR 0006](../decisions/0006-backend-and-frontend-languages.md) records the decision.

## Performance targets

| Operation | Target |
| --- | --- |
| Card search | An answer in less than 150 ms, on about 100,000 printings |
| Deck of 100 cards | Load and analysis in less than 1 s |
| Collection of 50,000 cards | No noticeable loss of speed |
| Action in an online game | The action reaches the other players in less than 200 ms |

## Horizontal scaling with Kubernetes

- **Stateless API**: the API keeps no session in memory, and it uses token authentication.
  Any replica of the API can serve any request.
- **Real time**: SignalR uses a Redis backplane, so that the players of one game session can use different replicas.
- **Background tasks**: separate workers run the Scryfall synchronization, auto-tag and the tasks of modules.
  The workers scale independently of the API.
- **Quotas**: Redis counts the requests for the quotas, so that the counts agree on all replicas.
- **Helm chart**: the project delivers a Helm chart when Kubernetes hosting becomes relevant, on OVH or another provider.

## Local development

- One command does each action on a local instance: `just up` creates or updates it, `just reset` resets it, and `just down` destroys it.
  Docker Compose runs the services, and `just` gives the commands.
  [ADR 0016](../decisions/0016-local-infrastructure.md) records this choice.
- A demo data set contains a subset of the cards, and example accounts and decks.
  With this data set, a developer can start work without a download of all the Scryfall data.
- Several local instances can run at the same time, each with its own project prefix.
  For example, a developer can test a plugin or a migration in a separate instance.

The [getting started guide](../development/getting-started.md) gives all the commands.

## Stack

The **Alternative** column gives an option that the project did not choose, or an option for later.

| Layer | Choice | Alternative |
| --- | --- | --- |
| API | C# and ASP.NET Core on .NET 10, the current LTS version ([ADR 0006](../decisions/0006-backend-and-frontend-languages.md)) | Go |
| Real time | SignalR with a Redis backplane | Raw WebSocket |
| Database | PostgreSQL, separate schemas, shard directory ([ADR 0008](../decisions/0008-postgresql-schemas-and-shard-directory.md)) | Citus for native sharding later |
| Migrations | Versioned SQL scripts with DbUp or grate. The choice of the tool is still open. | EF Core Migrations |
| Search | A parser that converts the Scryfall syntax to SQL, with PostgreSQL indexes | Meilisearch as a complement |
| Cache, quotas, queues | Redis. Hangfire or dedicated workers for the background tasks. | RabbitMQ |
| Front end | TypeScript, React and Vite, PWA ([ADR 0015](../decisions/0015-frontend-react-vite-i18next.md)) | SvelteKit (not chosen) |
| Plugins and modules | Plugins: TypeScript in a browser sandbox. Modules: C# or TypeScript services in containers. | Internal modules as .NET libraries |
| Observability | OpenTelemetry, Prometheus, Grafana | — |
| Deployment | Docker Compose and just ([ADR 0016](../decisions/0016-local-infrastructure.md)) for local instances. Helm on Kubernetes. | .NET Aspire for local instances (not chosen) |
