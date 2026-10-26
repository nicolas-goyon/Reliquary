# Changelog

This file lists the notable changes of each release.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow [Semantic Versioning](https://semver.org).

## Unreleased

### Added

- Repository rules, the English documentation with checks for ASD-STE100, and the backlog of the roadmap.
- .NET 10 solution skeleton with the five core components and the architecture tests.
- Web app skeleton with a French and English interface.
- Local infrastructure with PostgreSQL and Redis, and CI for the backend, the web app and the documentation.
- Database platform: connections through the shard directory, SQL migrations with DbUp, domain events through an outbox, and scheduled jobs.
- Worker process and the command line tool `reliquary`, with the command `migrate`.
- API conventions: problem details with stable error codes, paging with cursors, and JSON settings.
- OpenTelemetry traces, metrics and logs, Prometheus endpoints, readiness checks for PostgreSQL and Redis, and an optional Grafana service.
- Card reference data: sets, Oracle cards with all their faces and names, printings, tags, rulings and MTGJSON identifiers.
  A scheduled job imports them every 12 hours.
- Card endpoints: a card by its identifier or by any name, its printings and its rulings, a printing, and the sets.
- The CLI commands `jobs list` and `jobs run`.
- Accounts: registration with an email check, sign-in with a password or with Google, GitHub and Discord, and password reset.
  Web sessions use a short access token and a refresh cookie.
- Personal API keys with daily quotas for each key and each account. The quotas use Redis, so all the API replicas share them.
- Emails in English and French, and a Mailpit service in the local infrastructure.
