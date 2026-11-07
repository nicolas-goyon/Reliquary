# Deploy with Docker Compose

This guide tells how to install, update and restore a deployed instance of Reliquary with Docker Compose.
The pre-production instance of the maintainer uses this procedure.
Terms follow the [glossary](../glossary.md).
[ADR 0042](../decisions/0042-deployed-instances-and-updates.md) gives the reasons for the design.

## Difference with the local instance

| Item | Local instance (`just up`) | Deployed instance (`reliquary-update`) |
| --- | --- | --- |
| Use | Development and tests of a branch | Daily use, pre-production |
| Images | Built from the source code | Published images of a channel or a release |
| Data | Demo accounts and 342 demo cards | Your accounts and all the cards |
| Secrets | Public test values | New secrets in your `.env` file |
| Address | <http://localhost:8080> | <http://localhost:8090> |
| `just reset` | Deletes the data | Has no effect on the deployed instance |

The two instances can run at the same time.

## Requirements

- Docker with the Compose plugin 2.17 or later.
- A POSIX shell. On Windows, use WSL.
- About 4 GB of free disk space for the card data, and space for the backups.

## Channels and versions

| Value | Images |
| --- | --- |
| `develop` | The last build of the branch `develop` |
| `latest` | The last release |
| `0.2.0` | The release 0.2.0. This value never changes. |

## Install

### With a clone of the repository

1. Run the command:

   ```shell
   just preprod-install develop
   ```

   The command copies the script to `~/reliquary-preprod` and makes the file `~/reliquary-preprod/.env` with new secrets.
   To use a different folder, set `RELIQUARY_PREPROD_HOME`.

2. Read `~/reliquary-preprod/.env`. Set `RELIQUARY_ADMINISTRATOR_EMAIL` to your email address.
3. Run the command:

   ```shell
   just preprod-update
   ```

4. Open <http://localhost:8090> and make your account.

### With no clone

1. Make a folder for the instance, and get the script from the CLI image:

   ```shell
   mkdir ~/reliquary-preprod && cd ~/reliquary-preprod
   docker run --rm --entrypoint cat ghcr.io/nicolas-goyon/reliquary-cli:develop /app/deploy/reliquary-update > reliquary-update
   chmod +x reliquary-update
   ```

2. Make the `.env` file with new secrets:

   ```shell
   ./reliquary-update --init develop
   ```

3. Read `.env`. Set `RELIQUARY_ADMINISTRATOR_EMAIL` to your email address.
4. Install the instance:

   ```shell
   ./reliquary-update
   ```

5. Open <http://localhost:8090> and make your account.

At its first start, the worker imports all the cards from Scryfall. This import takes some minutes.
The card search gives few results until the import is complete.
Mailpit at <http://localhost:18035> keeps the emails of the instance.

## Update

Run one of these commands in the folder of the instance:

| Command | Effect |
| --- | --- |
| `./reliquary-update` | Update to the channel or the version of `RELIQUARY_VERSION` in `.env` |
| `./reliquary-update develop` | Update to the last build of `develop` |
| `./reliquary-update latest` | Update to the last release |
| `./reliquary-update 0.2.0` | Update to the release 0.2.0 |
| `./reliquary-update --check` | Tell if a different version is available. Change nothing. |
| `./reliquary-update --status` | Show the running version, the containers and the restore points |

With a clone of the repository, `just preprod-update`, `just preprod-update develop` and `just preprod-status` do the same.

The update does these steps:

1. Pull the images, and read the Compose file of the new version from its CLI image.
2. Back up the databases to `backups/<date>-<old version>`, and record a restore point with the old images.
3. Stop the API, the worker and the web app.
4. Apply the migrations of the new version.
5. Start the new version, and wait until the API and the worker are ready.

When step 4 or step 5 fails, the script restores the backup and starts the old version again.
Then it stops with an error message. Read the messages, and tell the maintainers.

When the images did not change, the script changes nothing.

## Roll back

To go back to the version before the last update:

```shell
./reliquary-update --rollback
```

The command does these steps:

1. Make a backup `<date>-before-rollback` of the current data.
2. Restore the backup of the last restore point.
3. Start the images of the last restore point.

The changes after the update are not in the restored data. The backup of step 1 keeps them.
Run the command again to go back one more version.

## Backups

- The script makes a backup before each update and before each rollback.
- `./reliquary-update --backup LABEL` makes a backup `<date>-LABEL` at any time.
- The script keeps the last `RELIQUARY_KEEP_BACKUPS` backups (default 10), and deletes the older backups.
- The backups are in the folder `backups`. Copy them to a different computer or to a storage service.

[Backup and restore](../development/backup-and-restore.md) gives the content of a backup.

## Settings

The file `.env` contains the settings. The comments of the file give each setting.
After a change, run `./reliquary-update`. When the images did not change, the command starts the containers again with the new settings.

| Setting | Use |
| --- | --- |
| `RELIQUARY_VERSION` | The default channel or version of `reliquary-update` |
| `RELIQUARY_BIND_ADDRESS` | `127.0.0.1` for this computer only. `0.0.0.0` to open the instance to your network, for example for a phone. |
| `RELIQUARY_PUBLIC_URL` | The address that the users open. Change it with `RELIQUARY_BIND_ADDRESS`. |
| `RELIQUARY_SMTP_*` | A real email server. The default is Mailpit. |
| `RELIQUARY_SIGNING_KEY` | The key of the access tokens. When you change it, all the users must sign in again. |

Do not change `RELIQUARY_POSTGRES_PASSWORD` after the installation. The database keeps the first password.

## Files of the instance

| Path | Content |
| --- | --- |
| `reliquary-update` | The script |
| `.env` | The settings and the secrets. Keep this file private. |
| `.state/current/` | The image digests and the Compose file of the running version |
| `.state/restore-points/` | One folder for each backup before an update: the image digests and the Compose file |
| `backups/` | The backups of the databases |

The data of the databases is in the Docker volumes `reliquary-preprod_postgres-data` and `reliquary-preprod_redis-data`.

## Remove the instance

This procedure deletes all the data of the instance.

1. Copy the backups that you want to keep.
2. In the folder of the instance, run the command:

   ```shell
   docker compose --project-directory . --file .state/current/compose.yaml --env-file .env --env-file .state/current/images.env down --volumes
   ```

3. Delete the folder of the instance.
