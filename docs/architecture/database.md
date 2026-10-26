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
- A shard directory gives the database of each owner.
- For each request, the application gets the database connection for the `owner_id` from the shard directory.

### Shard directory

The table `users.shard_directory` contains one row for each owner:

| Column | Content |
| --- | --- |
| `owner_id` | The owner of the data |
| `shard` | The name of the shard database |
| `read_only` | `true` while the data of the owner moves to another database |
| `assigned_at` | The time of the assignment |

The configuration gives the connection string of each shard, by name, in `Database:Shards`.
The table contains only the names, so the passwords stay out of the database.
Without a shard in the configuration, the instance has one shard, `main`, in the users database.

A new owner goes to the shard `Database:DefaultShard`.
The core component that creates the owner calls `IShardDirectory.AssignAsync` in its own transaction.

A core component opens a content connection with `IDatabase.OpenContentAsync(ownerId, access)`:

- With the access `Read`, the connection opens also during a move.
- With the access `Write`, the method fails during a move, so no change goes to the old copy.
- The directory keeps each assignment in memory for one minute (`Database:DirectoryCacheDuration`).
  A move must wait for this time after it makes the owner read-only.

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

Reliquary applies ordered SQL scripts with DbUp.
[ADR 0017](../decisions/0017-data-access-and-migrations.md) gives the reasons for this choice.

### Migration sets

A migration set is an ordered list of SQL scripts that one owner applies to one schema.
The owner is the platform, a core component or a module.

- Each script is an embedded resource of the assembly of the owner, in the folder `Migrations/<schema>`, for example `Migrations/users/0001_accounts.sql`.
- The name of a script starts with a number of four digits: `0001_init.sql`, `0002_decks.sql` and the next scripts.
- Each owner numbers its own scripts. Two owners never share a number.
- A core component declares its sets in `ICoreComponent.MigrationSets`.
- The runner applies the sets of the platform first, then the sets of the core components in their order of registration.

Each schema has a journal table, `<schema>.schema_migrations`.
It contains the name of each applied script, in the form `<owner>/<file>`.

### Databases of each set

| Target | Databases |
| --- | --- |
| `Users` | The users database |
| `Reference` | The reference database |
| `Content` | Each shard database |

### Apply the migrations

- The command `reliquary migrate` of the CLI applies the scripts to all databases. Run it before each deployment.
- The command `reliquary migrate --check` lists the pending scripts. Its exit code is 1 when a script is pending.
- When `Database:MigrateOnStartup` is `true`, the API and the worker apply the scripts when they start. The development settings enable this option.
- An advisory lock in each database stops two hosts that apply the scripts at the same time.
- Each script runs in its own transaction. When a script fails, the runner stops, and the database keeps the scripts before it.

### Rules for the scripts

- Never change a script after its merge. Add a new script.
- A migration must never break the previous version of the API during a progressive deployment.
  For example, add a column in one release, and remove the old column in a later release.
- A script uses only its own schema, and never the tables of another owner.
- Each module has its own sets for its `mod_<id>` schema. The same runner applies them.

[ADR 0008](../decisions/0008-postgresql-schemas-and-shard-directory.md) gives the reasons for the schemas and the shard directory.
