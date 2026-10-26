# 0008. PostgreSQL with separate schemas and a shard directory

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

- One command must create a local instance, with no operational cost.
- Later, the architecture must accept Kubernetes replicas and a split of the database, without a rewrite.
- All users share the card reference data, in read-only mode.
- Each piece of user data belongs to one owner.

## Decision

- PostgreSQL is the database.
- **Vertical split**: from the start, the data is in separate schemas:
  - `reference` for the card data, which all users share in read-only mode
  - `users` for the accounts, the API keys and the quotas
  - `content` for the decks and the collection
  - `mod_<id>` for the tables of each module
- **Horizontal split**: each content table has an `owner_id` column.
  A shard directory gives the database of each user, or of each range of users.
  For each request, the application gets the database connection for the `owner_id`.
- At the start, the shard directory contains one database only.
- All identifiers are UUIDv7 values, not auto-increment integers.
- **Migrations**: the migrations are an ordered list of versioned SQL scripts.
  A tool applies each migration to all the databases of the shard directory.
  A migration never breaks the previous version of the API during a progressive deployment.
- The choice of the migration tool is still open: DbUp or grate.
  A task of milestone M0 chooses the tool and records the choice in an ADR.
- The project does not use EF Core Migrations.
- Citus is an option for native sharding later.

## Consequences

- Each schema can move to its own database server later.
- Reliquary can move the data of a user to another database while the instance runs.
  The [database](../architecture/database.md) file gives the steps.
- With these moves, an operator can empty one database into another database.
  For example, an instance can move from a home server to a VPS, and then to Kubernetes.
- The application needs the `owner_id` of each request to user data, so that it can choose the database.
- With UUIDv7 identifiers, Reliquary can copy data between databases with no collision.
- With one database at the start, the mechanism exists, but it adds no operational cost.
- The tables of modules follow the same rules: an `owner_id` on all user data, and UUIDv7 identifiers.
