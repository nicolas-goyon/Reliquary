# 0042. Deployed instances and their updates

- Status: Accepted
- Date: 2026-11-07
- Deciders: @nicolas-goyon

## Context

The local instance of [ADR 0024](0024-container-images.md) builds the images from the source code and loads the demo data.
It is good for development, but it is not good for a person who uses Reliquary every day:

- `just reset` and a change of branch remove or change the data.
- The instance has public test settings: a known signing key and demo accounts with public passwords.
- An update needs a clone of the repository, the right branch and a build of all the images.

The maintainer needs a pre-production instance on his computer with Docker.
It must keep its data, and one command must update it to the last build of `develop` or to the last release.
The production instance will run on Kubernetes ([#64](https://github.com/nicolas-goyon/reliquary/issues/64), M4) with the same images.

## Decision

### Images

- The workflow `images.yml` publishes the images `api`, `worker`, `cli` and `web` to `ghcr.io/nicolas-goyon/reliquary-<host>`, for `linux/amd64` and `linux/arm64`.
- Each image has a channel tag and a fixed tag:

  | Event | Tags | Version in the image |
  | --- | --- | --- |
  | Push to `develop` | `develop`, `sha-<commit>` | `X.Y.(Z+1)-develop.<run>`, where `X.Y.Z` is the last release |
  | Release tag `vX.Y.Z` ([ADR 0043](0043-releases-with-release-please.md)) | `X.Y.Z`, `X.Y`, `latest` | `X.Y.Z` |

- `/api/v1/status` gives the version and the commit. The footer of the web app gives the version of the web app.
- The images work on Docker Compose and on Kubernetes with no change:
  - The containers run as users that are not root, with a read-only root file system and a tmpfs on `/tmp`.
    The web container also needs a tmpfs on `/etc/nginx/conf.d`.
  - The web image reads the address of the API from `RELIQUARY_API_URL`, and its DNS resolver from `RELIQUARY_DNS_RESOLVER` or from `/etc/resolv.conf`.
  - The CLI applies the migrations as a separate step, before the new API and the new worker start.

### Deployed instance with Docker Compose

- `deploy/compose.yaml` describes a deployed instance. It uses the published images and has no `build` section.
  The instance does not start without a signing key and a database password. PostgreSQL and Redis keep their data in volumes.
  Redis keeps the keys that protect the cookies, so its data must stay after an update.
- The CLI image contains `deploy/compose.yaml`, `deploy/.env.example` and `deploy/reliquary-update`.
  Thus each version brings its own Compose file, and the folder of an instance needs no clone of the repository.
- The script `reliquary-update` is the only tool for the operations of the instance:
  - `reliquary-update VERSION` installs or updates the instance. VERSION is a channel (`develop`, `latest`) or a release number.
  - The script pins each image by its digest. A channel tag that moves does not change a running instance.
  - Before an update, the script makes a backup and a restore point: the digests and the Compose file of the old version.
  - When the migrations or the start of the new version fail, the script restores the backup and starts the old version again.
  - `reliquary-update --rollback` starts the version of the last restore point with the data of its backup. It makes a backup of the current data first.
  - After a successful update, the script replaces itself with the script of the new version.
- The name of the Compose project is `reliquary-preprod` by default. The recipes of the local instance refuse the instance name `preprod`.

### Migration rules

- A migration is append-only. A pull request must not change, rename or delete a migration file that `develop` contains.
  The deployed instances of the channel `develop` already applied it, and the migration runner records only the name of a script.
- A migration that deletes a column, a table or data comes in two releases:
  1. The code stops to use the column or the table.
  2. A migration of a later release deletes it.
- A migration must work on a database with data, not only on an empty database.
- The demo data is for the development instance only. A deployed instance never loads it.
- `reliquary restore --replace` deletes all the schemas of each target database before the restore.
  Thus a rollback works also after an update that added tables.

## Consequences

- The maintainer installs the pre-production instance one time, then updates it with `reliquary-update develop` or `reliquary-update latest`.
- A failed update gives the old version and the data of the backup. The changes after the backup are lost only on a rollback, and a backup keeps them.
- The gate test `UpgradeKeepsData` installs a version, adds data, updates, rolls back and checks the data in the CI.
- An update stops the application for some seconds to some minutes. There is no update with no interruption.
- The Helm chart of M4 uses the same images, the same settings and the same migration step. The guide [Kubernetes](../deployment/kubernetes.md) lists the requirements.
- The backups stay on the same computer. The administrator must copy them to a different computer.
