# Versions and updates

This page tells how the versions of Reliquary change, and what an update does.
Terms follow the [glossary](../glossary.md).
[ADR 0042](../decisions/0042-deployed-instances-and-updates.md) and [ADR 0043](../decisions/0043-releases-with-release-please.md) give the reasons.

## Channels and release numbers

The CI publishes four images for each build: `reliquary-api`, `reliquary-worker`, `reliquary-cli` and `reliquary-web`, in `ghcr.io/nicolas-goyon`.

| Tag | Moves | Content | Use |
| --- | --- | --- | --- |
| `develop` | At each merge into `develop` | The last build of `develop` | Pre-production instance |
| `sha-<commit>` | Never | The build of one commit of `develop` | Tests of one commit |
| `latest` | At each release | The last release | Quick tests of the last release |
| `X.Y` | At each patch release | The last release `X.Y.*` | — |
| `X.Y.Z` | Never | One release | Production instance |

The footer of the web app and `/api/v1/status` give the version and the commit.
A build of `develop` has a version such as `0.2.1-develop.42`: it comes after the release `0.2.0` and before the next release.

## Release numbers

The releases follow [Semantic Versioning](https://semver.org). Before `1.0.0`:

- A new feature increases the minor number: `0.2.0` → `0.3.0`.
- A fix increases the patch number: `0.2.0` → `0.2.1`.
- A change that breaks the compatibility also increases the minor number. The release notes tell what to do.

The [changelog](https://github.com/nicolas-goyon/reliquary/blob/develop/CHANGELOG.md) and the [releases page](https://github.com/nicolas-goyon/reliquary/releases) list the changes of each release.

## Steps of an update

`reliquary-update VERSION` does these steps:

1. Pull the images of VERSION, and read the Compose file of VERSION from its CLI image.
2. Check the Compose file with the `.env` file. When a setting is missing, the script stops and changes nothing.
3. Stop the API, the worker and the web app.
4. Back up the databases, and record a restore point with the old images.
5. Apply the migrations of VERSION.
6. Start VERSION, and wait until the API and the worker are ready.
7. Delete the old backups after the number `RELIQUARY_KEEP_BACKUPS`.

When a step after step 3 fails or stops, the script restores the backup of step 4 and starts the old version again.

You can update across several releases at one time, for example from `0.2.0` to `0.5.0`. The migrations of all the releases between them run in order.

## Roll back

`reliquary-update --rollback` starts the version of the last restore point with the data of its backup.
The changes after this backup are not in the restored data. A backup `<date>-before-rollback` keeps them.

A rollback is the only way to go back to an older version. Do not install an older version over a newer database: the old code does not know the new tables and columns.

## Migration rules

These rules keep the updates safe. The CI checks the first rule on each pull request.

- A migration is append-only. A pull request never changes, renames or deletes a migration file that `develop` contains.
- A migration that deletes a column, a table or data comes in two releases. First the code stops to use it, then a later release deletes it.
- A migration must work on a database with data.
- The CI test `UpgradeKeepsData` installs the last release, adds data, updates to the new build, rolls back, and checks the data at each step.
