# Documentation

This folder contains the documentation of Reliquary.
All documents are in English and follow the [writing rules](contributing/writing-style.md).

## Product

| Document | Content |
| --- | --- |
| [Specification](specification/README.md) | What Reliquary must do, for each feature area |
| [Roadmap](roadmap.md) | The milestones M0 to M4, their gates, and the work after M4 |
| [Glossary](glossary.md) | One meaning for each term, with the French interface term |

## Architecture

| Document | Content |
| --- | --- |
| [Overview](architecture/overview.md) | Core components, API, real time and extensions |
| [Data model](architecture/data-model.md) | Card reference data and user data |
| [Database](architecture/database.md) | Schemas, shard directory and migrations |
| [Domain events](architecture/domain-events.md) | Event names, versions, outbox and consumers |
| [API conventions](architecture/api-conventions.md) | Versions, JSON, paging, sort, filters and error codes |
| [Collection](architecture/collection.md) | Locations, entries, movements and the interfaces for the decks |
| [Card search](architecture/card-search.md) | The search endpoint, the Scryfall syntax, the rules of the search and its indexes |
| [Extensions](architecture/extensions.md) | Plugins and modules |
| [Internal API](architecture/internal-api.md) | The versioned API for the modules, its OpenAPI document and the catalog of the domain events |
| [Service accounts](architecture/service-accounts.md) | Service accounts, delegations, delegation tokens and scopes |
| [Technology stack](architecture/tech-stack.md) | Technologies and performance targets |
| [Decision records](decisions/README.md) | The reasons for each important decision |

## Development

| Document | Content |
| --- | --- |
| [Getting started](development/getting-started.md) | How to run Reliquary on your computer, and the code layout |
| [Translation guide](development/translations.md) | Rules for the French and English interface texts |
| [Web app](development/web-app.md) | The installable web app, its service worker, the layout for small screens, the keyboard shortcuts, the themes and the accessibility checks |
| [Observability](development/observability.md) | Traces, metrics, logs, health endpoints and Grafana |
| [Backup and restore](development/backup-and-restore.md) | The backup of all the databases of an instance, and its restore |
| [Card data](development/card-data.md) | Import of the card data from Scryfall and MTGJSON, and small datasets |
| [Search benchmark](development/search-benchmark.md) | The measure of the response time of the card search, with the hardware and the results |
| [Writing style](contributing/writing-style.md) | The ASD-STE100 rules for all English text |
| [Contributing guide](../CONTRIBUTING.md) | Branches, pull requests and checks |
