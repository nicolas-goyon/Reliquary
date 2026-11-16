# 0054. The backend uses the latest version of .NET, LTS or STS

- Status: Accepted
- Date: 2026-11-16
- Deciders: @nicolas-goyon

## Context

[ADR 0006](0006-backend-and-frontend-languages.md) says that the backend uses an LTS version of .NET.
Microsoft gives each .NET version one of two types of support:

- LTS (long-term support): 3 years of support. The even versions are LTS.
- STS (standard-term support): 2 years of support. The odd versions are STS.

Microsoft released .NET 11, an STS version, on 2026-11-10.
Microsoft supports .NET 11 until November 2028.
Microsoft also supports .NET 10, the LTS version, until November 2028.
Thus, the two versions get security fixes for the same time.

Dependabot proposes new major versions of packages that need .NET 11:

- the ASP.NET Core 11 packages and `Microsoft.Extensions.TimeProvider.Testing` 11
- `AspNet.Security.OAuth.Discord` and `AspNet.Security.OAuth.GitHub` 11
- the container images `dotnet/sdk:11.0` and `dotnet/aspnet:11.0`

With the LTS rule, the project must keep these updates closed for one year.
The project then does one large update to .NET 12, with two years of breaking changes at the same time.

The options are:

| Option | Advantages | Disadvantages |
| --- | --- | --- |
| Keep .NET 10 until .NET 12 | Fewer updates | One large update in 2027. No new features of .NET 11. The Dependabot updates stay closed. |
| Use .NET 11 now | Small updates each year. The project gets the fixes and the new features of each version. | One update each year. Some packages can be late for a new version. |

## Decision

- The backend uses the latest version of .NET, LTS or STS.
  This decision replaces the rule "an LTS version of .NET" of [ADR 0006](0006-backend-and-frontend-languages.md).
- The backend uses .NET 11 from 2026-11-16.
- The project updates to the next version of .NET in the months after its release, when the packages of the project support it.

## Consequences

- The project does one update of .NET each year, in November or December.
  The next update is .NET 12 in November 2027.
- The version that the project uses always has Microsoft support.
- `global.json`, `Directory.Build.props`, the `Dockerfile` and the [technology stack](../architecture/tech-stack.md) give the current version.
- An update can need changes of the code for the breaking changes of the new version.
  The pull request of the update lists the breaking changes that it checked.
