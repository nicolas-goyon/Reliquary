# Database

This file describes how Reliquary splits its PostgreSQL data, moves the data of a user to another database, and applies the migrations.
Terms follow the [glossary](../glossary.md).

## Vertical split

From the start, Reliquary puts its data in separate PostgreSQL schemas:

| Schema | Content |
| --- | --- |
| `reference` | The card reference data. All users share it in read-only mode. |
| `users` | Accounts, API keys, quotas, service accounts and delegation tokens |
| `content` | Decks and the collection |
| `mod_<id>` | The tables of one module. Each module has its own schema. |

Later, each schema can move to its own database server.

## Horizontal split

Reliquary prepares a horizontal split of the user data:

- All the content tables have an `owner_id` column.
- A shard directory gives the database of each user, or of each range of users.
- For each request, the application gets the database connection for the `owner_id` from the shard directory.

## Move a user to another database

Reliquary can move the data of one user to another database while the instance runs.
The user cannot change their data during the move, but the move is short.
To move a user, do these steps:

1. Make the data of the user read-only.
2. Copy the data of the user to the new database.
3. Change the shard directory, so that it gives the new database for the user.
4. Make the data of the user writable again, in the new database.
5. Delete the old copy of the data.

With these moves, the instance administrator can empty one database into another database, one user at a time.
For example, an instance can move from a home server to a VPS, and then to Kubernetes.

## Identifiers

All identifiers are UUIDv7 values, not auto-increment integers.
With these identifiers, Reliquary can copy data between databases with no collision.

## One database at the start

At the start, the shard directory contains one database only.
The mechanism exists, but it adds no operational cost.

## Migrations

- The migrations are an ordered list of versioned SQL scripts, as in Go projects: `0001_init.sql`, `0002_decks.sql` and the next scripts.
- A migration tool applies the scripts when the application starts, or when the instance administrator runs a separate command.
- The choice of the migration tool is still open: DbUp or grate.
  The M0 task [#14](https://github.com/nicolas-goyon/reliquary/issues/14) chooses the tool and records the choice in an ADR.
- The tool applies each migration to all the databases of the shard directory.
- A migration must never break the previous version of the API during a progressive deployment.
- Each module has its own SQL migrations for its `mod_<id>` schema.
  The same tool applies the migrations of the core and the migrations of the modules.

[ADR 0008](../decisions/0008-postgresql-schemas-and-shard-directory.md) gives the reasons for these rules.
