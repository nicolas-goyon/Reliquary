# 0006. Backend in C# and ASP.NET Core, front end and plugins in TypeScript

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The project needs a language for the backend, and a language for the front end and the plugins.
The domain is rich: decks, deck versions, variants and the collection.
The specification compares two options for the backend: C# with ASP.NET Core, and Go.
It gives these points:

- ASP.NET Core is one of the fastest web frameworks in public benchmarks.
- ASP.NET Core runs well in Linux containers.
- ASP.NET Core includes the features that the project needs:
  - SignalR for the real time of online games
  - built-in rate limiting for the quotas
  - EF Core or raw SQL
  - OpenTelemetry
- Go would use less memory.
- C# will be more productive for a rich domain.
- The maintainer knows C# better than Go.

## Decision

- The backend uses C# and ASP.NET Core, on an LTS version of .NET.
  At the start, the backend uses .NET 10, the current LTS version.
- The front end uses TypeScript.
  [ADR 0015](0015-frontend-react-vite-i18next.md) gives the front-end framework.
- Plugins use TypeScript.
- Modules can use C# or TypeScript, as [ADR 0010](0010-plugins-and-modules.md) says.
- Go is the rejected alternative for the backend.

## Consequences

- The backend uses the standard features of ASP.NET Core: SignalR for the game sessions, the built-in rate limiting for the quotas, and OpenTelemetry.
- The backend uses more memory than a backend in Go.
- Backend contributors write C#.
  Front-end contributors and plugin authors write TypeScript.
- The [technology stack](../architecture/tech-stack.md) gives the full stack.
