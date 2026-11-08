# Pre-production instance

A pre-production instance runs the published images on your computer with Docker Compose, and keeps its data after each update.
The maintainer uses it to try the next release with real data.
The script `reliquary-update` installs, updates, rolls back and backs up the instance.
Terms follow the [glossary](../glossary.md).
[ADR 0042](../decisions/0042-deployed-instances-and-updates.md) gives the reasons for the design.

The [overview](index.md) compares this mode with the other modes.
A [production instance on a server](production.md) uses the same script with more settings.

## Requirements

- Docker with the Compose plugin 2.17 or later. [Install the software](requirements.md#install-the-software) tells how to install it on Linux and on Windows.
- A POSIX shell for the script `reliquary-update`. On Windows, use PowerShell with the script `reliquary-update.ps1`, or the Ubuntu terminal of WSL 2.
- About 4 GB of free disk space for the card data, and space for the backups.

[Requirements](requirements.md) gives the details.

## Versions

| Value | Images |
| --- | --- |
| `develop` | The last build of the branch `develop`. Use it for a pre-production instance. |
| `latest` | The last release |
| `0.2.0` | The release 0.2.0. This value never changes. |

[Versions and updates](versions-and-updates.md) tells how the versions change.

## Install

Select your system:

/// tab | Linux

Open a terminal of your desktop, then follow the steps below. The instance goes in the folder `~/reliquary-preprod`.

///

/// tab | Windows

Use PowerShell and the script `reliquary-update.ps1`. You need only Docker Desktop.
[Install the software](requirements.md#install-the-software) tells how to install it and how to let PowerShell run the script.

1. Start Docker Desktop, and open PowerShell.
2. Make a folder for the instance, and download the script [`reliquary-update.ps1`](https://nicolas-goyon.github.io/reliquary/downloads/reliquary-update.ps1):

   ```powershell
   New-Item -ItemType Directory -Path "$HOME\reliquary-preprod" | Out-Null
   Set-Location "$HOME\reliquary-preprod"
   Invoke-WebRequest -Uri https://nicolas-goyon.github.io/reliquary/downloads/reliquary-update.ps1 -OutFile reliquary-update.ps1
   Unblock-File .\reliquary-update.ps1
   ```

   The script works with all the versions. After each update, it replaces itself with the script of the new version.
   When you download the script with the link, put it in the folder of the instance, then run `Unblock-File`. Without this command, PowerShell asks for a signature.

3. Make the `.env` file with new secrets:

   ```powershell
   .\reliquary-update.ps1 --init develop
   ```

4. Open `.env` with `notepad .env`. Set `RELIQUARY_ADMINISTRATOR_EMAIL` to your email address.
5. Install the instance:

   ```powershell
   .\reliquary-update.ps1
   ```

6. Open <http://localhost:8090> and make your account.

The script `reliquary-update.ps1` has the same commands as `reliquary-update`. On this page, replace `./reliquary-update` with `.\reliquary-update.ps1`.
For example, `.\reliquary-update.ps1 --rollback`.
The instance is in `C:\Users\<your user name>\reliquary-preprod`. Only your user can open this folder.

To use the Ubuntu terminal of WSL 2 instead, follow the steps for Linux in the Ubuntu terminal.
Do not use Git Bash: see [Git Bash](requirements.md#git-bash).

///

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

1. Make a folder for the instance, and download the script [`reliquary-update`](https://nicolas-goyon.github.io/reliquary/downloads/reliquary-update):

   ```shell
   mkdir ~/reliquary-preprod && cd ~/reliquary-preprod
   curl --fail --location --output reliquary-update https://nicolas-goyon.github.io/reliquary/downloads/reliquary-update
   chmod +x reliquary-update
   ```

   The script works with all the versions. After each update, it replaces itself with the script of the new version.

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

## Start with the computer

The containers start again when Docker starts. Docker must start with the computer:

/// tab | Linux

```shell
sudo systemctl enable docker
```

///

/// tab | Windows

In Docker Desktop, open **Settings → General**, and select **Start Docker Desktop when you sign in to your computer**.
The instance is available some seconds after Docker Desktop starts.

///

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
| `./reliquary-update --recover` | Finish an update or a rollback that stopped. See [Recover](#recover). |

With a clone of the repository, `just preprod-update`, `just preprod-update develop` and `just preprod-status` do the same.

The update does these steps:

1. Pull the images, and read the Compose file of the new version from its CLI image.
   Check the Compose file with the settings of `.env`. When a setting is missing, the script stops here and changes nothing.
2. Stop the API, the worker and the web app.
3. Back up the databases to `backups/<date>-<old version>`, and record a restore point with the old images.
4. Apply the migrations of the new version.
5. Start the new version, and wait until the API and the worker are ready.

When a step after step 2 fails, the script restores the backup and starts the old version again.
It does the same when the script stops, for example with Ctrl+C.
Then it stops with an error message. Read the messages, and tell the maintainers.

When the images did not change, the script changes nothing.

## Roll back

To go back to the version before the last update:

```shell
./reliquary-update --rollback
```

The command does these steps:

1. Stop the API, the worker and the web app.
2. Make a backup `<date>-before-rollback` of the current data.
3. Restore the backup of the last restore point.
4. Start the images of the last restore point.

When a step after step 1 fails, the script restores the backup of step 2 and starts the current version again.

The changes after the update are not in the restored data. The backup of step 1 keeps them.
Run the command again to go back one more version.

## Recover

The script records each update and each rollback until its end.
When the recovery of a failed update also fails (for example, Docker stops), the script tells you to run this command:

```shell
./reliquary-update --recover
```

The command restores the backup of the update or of the rollback, and starts the version before it.
Until then, the other commands that change the instance refuse to start.

## Backups

- The script makes a backup before each update and before each rollback.
- `./reliquary-update --backup LABEL` makes a backup `<date>-LABEL` at any time. The label contains only letters, digits, `.`, `_` and `-`.
- The date in the names is in UTC, for example `20261107T142233Z`.
- The script keeps the last `RELIQUARY_KEEP_BACKUPS` backups (default 10), and deletes the older backups.
  It deletes only the backups that it made, and never the backup of the last restore point.
- Only your user can open the folder `backups`, because the backups contain the data of all the accounts.
- The backups are in the folder `backups`. Copy them to a different disk, to a different computer or to a storage service.

To copy the backups to a different disk:

/// tab | Linux

```shell
rsync --archive backups/ /media/$USER/my-disk/reliquary-backups/
```

Replace `/media/$USER/my-disk` with the folder of your disk.

///

/// tab | Windows

In PowerShell, for example for the disk `D:`:

```powershell
New-Item -ItemType Directory -Force -Path D:\reliquary-backups | Out-Null
Copy-Item -Recurse -Force backups\* D:\reliquary-backups\
```

The disk `D:` does not keep the permissions of your user folder. Other users of the computer can maybe open the copy.

///

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
| `reliquary-update` | The script for Linux, macOS and WSL 2 |
| `reliquary-update.ps1` | The script for PowerShell on Windows |
| `.env` | The settings and the secrets. Keep this file private. |
| `.state/current/` | The image digests and the Compose file of the running version |
| `.state/restore-points/` | One folder for each backup before an update: the image digests and the Compose file |
| `.state/in-progress` | The update or the rollback that runs now, or that stopped. See [Recover](#recover). |
| `.state/lock` | The lock of a run. Delete it only when no other run of the script exists. |
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
