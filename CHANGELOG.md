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
