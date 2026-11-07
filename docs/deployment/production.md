# Production instance

A production instance is a server that other people use.
Reliquary gives two ways to run it:

- **Docker Compose on one server**, with the script `reliquary-update`. This page describes it.
- **Kubernetes.** The Helm chart comes with the milestone M4 ([#64](https://github.com/nicolas-goyon/reliquary/issues/64)).
  [Requirements for Kubernetes](kubernetes.md) gives what a deployment must give to the images.

Terms follow the [glossary](../glossary.md).

## Differences with a pre-production instance

A production instance uses the same files and the same script as a [pre-production instance](pre-production.md). These items change:

| Item | Pre-production instance | Production instance |
| --- | --- | --- |
| Version | The channel `develop` | A release number, for example `0.2.0` |
| Address | <http://localhost:8090> | `https://your-domain`, through a reverse proxy |
| Emails | Mailpit keeps them | A real email server sends them |
| Backups | On the same computer | Also on a different computer, each day |
| Updates | When you want | After you read the release notes, at a time with few users |

## Requirements

- A Linux server with Docker and the Compose plugin. See [Requirements](requirements.md).
- A domain name that points to the server.
- A reverse proxy with HTTPS. This page uses [Caddy](https://caddyserver.com), which gets the certificates automatically.
- An email server (SMTP) for the account emails.
- A place on a different computer for the backups.

The server must use Linux. Docker Desktop is not for servers.
You can manage the server from a Linux computer or from a Windows computer.

## Connect to the server

Run the commands of this page on the server, through SSH:

/// tab | Linux

```shell
ssh your-user@your-server
```

///

/// tab | Windows

Windows 10 and Windows 11 contain the SSH client. In PowerShell:

```powershell
ssh your-user@your-server
```

///

## Install

1. On the server, make a user for Reliquary, and add this user to the group `docker`:

   ```shell
   sudo adduser reliquary
   sudo usermod --append --groups docker reliquary
   ```

2. As this user, make the folder of the instance and get the script of the release:

   ```shell
   mkdir ~/reliquary && cd ~/reliquary
   docker run --rm --entrypoint cat ghcr.io/nicolas-goyon/reliquary-cli:0.2.0 /app/deploy/reliquary-update > reliquary-update
   chmod +x reliquary-update
   ./reliquary-update --init 0.2.0
   ```

   Replace `0.2.0` with the number of the last release.

3. Change these values in `.env`:

   | Setting | Value |
   | --- | --- |
   | `RELIQUARY_PROJECT` | `reliquary` |
   | `RELIQUARY_PUBLIC_URL` | `https://your-domain` |
   | `RELIQUARY_BIND_ADDRESS` | `127.0.0.1`. Only the reverse proxy opens the instance. |
   | `RELIQUARY_ADMINISTRATOR_EMAIL` | Your email address |
   | `RELIQUARY_SMTP_HOST`, `RELIQUARY_SMTP_PORT`, `RELIQUARY_SMTP_SECURITY`, `RELIQUARY_SMTP_USER_NAME`, `RELIQUARY_SMTP_PASSWORD` | The settings of your email server |
   | `RELIQUARY_EMAIL_FROM` | The sender address, for example `Reliquary <noreply@your-domain>` |
   | `RELIQUARY_REQUIRE_CHECKED_EMAIL` | `true` |
   | `RELIQUARY_ACCOUNTS_WITHOUT_EMAIL` | `false` |
   | `RELIQUARY_KEEP_BACKUPS` | The number of local backups, for example `14` |

   For the Google sign-in, set `RELIQUARY_GOOGLE_CLIENT_ID` and `RELIQUARY_GOOGLE_CLIENT_SECRET`.
   The authorized redirect URI is `https://your-domain/api/v1/auth/external/google/callback`.

4. Install the instance:

   ```shell
   ./reliquary-update
   ```

5. Configure the reverse proxy. With Caddy, the file `/etc/caddy/Caddyfile` contains:

   ```text
   your-domain {
       reverse_proxy 127.0.0.1:8090
   }
   ```

   Then reload Caddy: `sudo systemctl reload caddy`.
   The reverse proxy must send all the paths to the web container. The web container sends `/api` to the API.
   It keeps the headers `X-Forwarded-Proto` and `X-Forwarded-Host` of the reverse proxy.

6. Open `https://your-domain`, make the account of the administrator email address, and open the link of the email.

The port 18035 of Mailpit stays on `127.0.0.1`. With a real email server, Mailpit receives no email.

## Back up to a different computer

The script makes a backup before each update. Make a backup each day too, and copy the backups to a different computer.

1. Run `crontab -e` as the user of the instance, and add this line:

   ```text
   15 3 * * * cd ~/reliquary && ./reliquary-update --backup daily >> backup.log 2>&1
   ```

2. Copy the folder `backups` to a different computer each day, for example with `rsync` or `restic`.
   On the different computer:

   /// tab | Linux

   ```shell
   rsync --archive reliquary@your-server:reliquary/backups/ ~/reliquary-backups/
   ```

   To do it each day, add this command to the `crontab` of the different computer.

   ///

   /// tab | Windows

   In PowerShell:

   ```powershell
   New-Item -ItemType Directory -Force "$HOME\reliquary-backups" | Out-Null
   scp -r "reliquary@your-server:reliquary/backups/*" "$HOME\reliquary-backups\"
   ```

   The command `scp` copies all the backups each time. To do it each day, make a task in the Task Scheduler of Windows.

   ///

3. Also keep a copy of the `.env` file in a safe place. A backup is not usable without the database password and the signing key.

To restore a backup on a new server, install the same version, stop the API and the worker, and run the CLI restore.
[Backup and restore](../development/backup-and-restore.md) gives the procedure.

## Update

1. Read the release notes on the [releases page](https://github.com/nicolas-goyon/reliquary/releases).
2. Check the available version:

   ```shell
   ./reliquary-update --check latest
   ```

3. At a time with few users, update to the release number:

   ```shell
   ./reliquary-update 0.3.0
   ```

   The application stops during the backup and the migrations, usually for less than one minute.

4. Open `https://your-domain` and check the version in the footer.

When the update fails, the script restores the backup and starts the old version again.
To go back to the old version after a successful update, run `./reliquary-update --rollback`.
The data that the users changed after the update is then lost. The script keeps it in a backup `<date>-before-rollback`.

Do not use the channels `develop` or `latest` in production. A release number never changes, so each update is a decision.

## Security

- Keep `.env` and the folder `backups` private. The script makes the folder `backups` readable by its user only.
- Do not open the ports of PostgreSQL, Redis or Mailpit. The Compose file does not open them, except Mailpit on `127.0.0.1`.
- Update to each new release that fixes a security problem. [Security policy](https://github.com/nicolas-goyon/reliquary/blob/develop/SECURITY.md) tells how to report a problem.
