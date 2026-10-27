# Decision records

This folder contains the architecture decision records (ADRs) of Reliquary.
Each record gives the context, the decision and its consequences.
[ADR 0001](0001-record-architecture-decisions.md) gives the rules for the records, and [template.md](template.md) gives their format.

## Records

| Number | Decision | Status |
| --- | --- | --- |
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-license-agpl-3.0.md) | License: AGPL-3.0 for the core | Accepted |
| [0003](0003-languages.md) | Languages: English for the project, French and English for the interface | Accepted |
| [0004](0004-writing-rules.md) | Writing rules: ASD-STE100 Simplified Technical English | Accepted |
| [0005](0005-git-workflow.md) | Git workflow: develop, main and pull requests | Accepted |
| [0006](0006-backend-and-frontend-languages.md) | Backend in C# and ASP.NET Core, front end and plugins in TypeScript | Accepted |
| [0007](0007-modular-monolith.md) | Modular monolith for the core | Accepted |
| [0008](0008-postgresql-schemas-and-shard-directory.md) | PostgreSQL with separate schemas and a shard directory | Accepted |
| [0009](0009-single-instance-without-federation.md) | One instance without federation | Accepted |
| [0010](0010-plugins-and-modules.md) | Two extension levels: plugins and modules | Accepted |
| [0011](0011-plugin-network-allowlist.md) | Plugin network access through a declared list of domains | Accepted |
| [0012](0012-card-data-sources.md) | Card data sources and card images | Accepted |
| [0013](0013-two-level-locations.md) | Locations with two levels | Accepted |
| [0014](0014-product-scope.md) | Product scope | Accepted |
| [0015](0015-frontend-react-vite-i18next.md) | Front end: React, Vite and i18next | Accepted |
| [0016](0016-local-infrastructure.md) | Local infrastructure: Docker Compose and just | Accepted |
| [0017](0017-data-access-and-migrations.md) | Data access with Npgsql and Dapper, migrations with DbUp | Accepted |
| [0018](0018-domain-events-and-outbox.md) | Domain events through an outbox in each database | Accepted |
| [0019](0019-hosts-and-scheduled-jobs.md) | Three hosts and scheduled jobs in the worker | Accepted |
| [0020](0020-card-data-import.md) | Import of the card data from the bulk files | Accepted |
| [0021](0021-card-search.md) | Card search with the Scryfall syntax in PostgreSQL | Accepted |
| [0022](0022-authentication-and-api-keys.md) | Authentication, web sessions and API keys | Accepted |
| [0023](0023-extension-registry.md) | Extension registry and core API version | Accepted |
| [0024](0024-container-images.md) | Container images and the full local instance | Accepted |
| [0025](0025-multi-face-analysis-rules.md) | Analysis rules for cards with more than one face | Accepted |
| [0026](0026-deck-model.md) | Deck model of milestone M1 | Accepted |
| [0027](0027-deck-analysis.md) | Deck analysis on the server, with rules and tables as data in code | Accepted |
| [0029](0029-public-deck-read-model.md) | Read model of the public decks in the users database, filled by the domain events | Accepted |

## Open decisions

Each open question is a GitHub issue with the label [`type: decision`](https://github.com/nicolas-goyon/reliquary/labels/type%3A%20decision).
An ADR records the answer, and the pull request of the ADR closes the issue.

| Issue | Question | Milestone |
| --- | --- | --- |
| [#36](https://github.com/nicolas-goyon/reliquary/issues/36) | Analysis rules for cards with more than one face | M1 |
| [#58](https://github.com/nicolas-goyon/reliquary/issues/58) | License of the plugin SDK: MIT or Apache 2.0 | M4 |
| [#67](https://github.com/nicolas-goyon/reliquary/issues/67) | Rights of shared collections and card reservation | Later |
| [#72](https://github.com/nicolas-goyon/reliquary/issues/72) | Quota threshold and price above the free quota | Later |
