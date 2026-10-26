# 0017. Data access with Npgsql and Dapper, migrations with DbUp

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

[ADR 0008](0008-postgresql-schemas-and-shard-directory.md) asks for ordered SQL scripts, and leaves the migration tool open: DbUp or grate ([#14](https://github.com/nicolas-goyon/reliquary/issues/14)).
The same tool must apply the scripts of the core and the scripts of the modules, to all the databases of the shard directory.
Each connection to user data must go through the shard directory.
The card search translates the Scryfall syntax into SQL, so the core needs full control of its SQL.

The options for the data access are:

- EF Core: it maps objects to tables, and it has its own migrations. It hides the SQL, and its migrations are not plain SQL scripts.
- Npgsql with Dapper: plain SQL, with a small mapper from rows to objects.

The options for the migration tool are:

- DbUp: a .NET library. The host calls it in its own code, so the host chooses the databases and the scripts.
- grate: a command line tool with more features, for example scripts that run each time. It is less simple to call from the code.

## Decision

- The core components use Npgsql and Dapper, with plain SQL.
- The core components open their connections through `IDatabase`.
  Content connections go through the shard directory.
- DbUp applies the migration scripts.
- The scripts are in migration sets: one set for each owner and schema.
  The owner is the platform, a core component or a module.
- Each schema has a journal table, `<schema>.schema_migrations`.
- The CLI command `reliquary migrate` applies the scripts to all databases.
  The option `Database:MigrateOnStartup` lets the API and the worker apply them when they start.
- An advisory lock in each database stops two hosts that apply the scripts at the same time.
- The application creates the UUIDv7 identifiers with `Guid.CreateVersion7()`, so the scripts do not need a database function.

## Consequences

- The SQL of each core component is visible and easy to review.
- Each core component writes its own mapping code. Dapper reduces this code.
- A module uses the same runner and the same rules for its `mod_<id>` schema.
- DbUp has no "down" scripts. To reverse a change, add a new script.
- The [database](../architecture/database.md) page describes the migration sets and the rules for the scripts.
