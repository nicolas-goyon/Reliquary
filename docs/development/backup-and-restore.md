# Backup and restore

This page gives the procedure to back up an instance and to restore it.
Terms follow the [glossary](../glossary.md).

## What the backup contains

The command `reliquary backup` saves each database of the instance with `pg_dump`:

- the users database, with the accounts, the shard directory, the deck directory and the read model of the public decks
- the reference database, with the card data
- each shard database of `Database:Shards`, with the decks of the users

When two roles use the same database, the backup contains the database one time.
The folder of the backup contains one file `database-N.dump` for each database and a file `manifest.json` with the roles of each database.

The backup does not contain Redis.
Redis contains only caches and counters. The worker copies the counters to PostgreSQL each minute.

## Requirements

- The commands `pg_dump` and `pg_restore` must be available. The Docker image of the CLI contains them.
- The major version of the tools must be the version of the PostgreSQL server, or a more recent version.
- To use tools in another folder, set `Backup:ToolsDirectory`, for example `/usr/lib/postgresql/18/bin`.

## Back up an instance

1. Give an empty folder for the backup.
2. Run the command:

   ```shell
   reliquary backup /backups/2026-10-28
   ```

3. Copy the folder to a different computer or to a storage service.

With the local instance, run `just backup NAME`. The backup goes to the folder `backups/NAME`.

You can back up a running instance. `pg_dump` gives a consistent copy of each database.
The copies of two different databases can be some seconds apart.

## Restore an instance

1. Stop the API and the worker.
2. Make sure that the configuration of the instance has a database for each role of the backup.
   For each shard of the backup, `Database:Shards` must have a shard with the same name.
3. Run the command:

   ```shell
   reliquary restore /backups/2026-10-28
   ```

   The command refuses an instance that has data. To replace the data, add `--replace`.

4. Apply the migrations of the current version:

   ```shell
   reliquary migrate
   ```

5. Start the API and the worker.

With the local instance, run `just restore-backup NAME`. It does the steps 1 to 5 and replaces the data.

## Test of the procedure

The CI job "Local instance" backs up the first instance, deletes it, restores it and checks a deck and the card data.
