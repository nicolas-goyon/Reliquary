# Production instance

A production instance is a server that other people use.
Reliquary gives two ways to run it:

- **Docker Compose on one server**, with the script `reliquary-update`. This page describes it.
- **Kubernetes**, with the Helm chart. [Production instance on Kubernetes](production-kubernetes.md) describes it.

Terms follow the [glossary](../glossary.md).

## Differences with a pre-production instance

A production instance uses the same files and the same script as a [pre-production instance](pre-production.md). These items change:

| Item | Pre-production instance | Production instance |
| --- | --- | --- |
| Version | The channel `develop` | A release number, for example `0.2.0` |
| Address | <http://localhost:8090> | `https://your-domain`, through a reverse proxy |
| Sign-in | A service, or the development sign-in | A service only: Google, GitHub or Discord |
| Backups | On the same computer | Also on a different computer, each day |
| Updates | When you want | After you read the release notes, at a time with few users |

## Requirements

- A Linux server with Docker and the Compose plugin. See [Requirements](requirements.md).
- A domain name that points to the server.
- A reverse proxy with HTTPS. This page uses [Caddy](https://caddyserver.com), which gets the certificates automatically.
- An OAuth client at Google, GitHub or Discord. The users sign in with this service. [Sign-in services](sign-in-services.md) tells how to make it.
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

2. As this user, make the folder of the instance, download the script and make the `.env` file:

   ```shell
   mkdir ~/reliquary && cd ~/reliquary
   curl --fail --location --output reliquary-update https://nicolas-goyon.github.io/reliquary/downloads/reliquary-update
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
   | `RELIQUARY_ADMINISTRATOR_EMAIL` | The email address of your account at the sign-in service |
   | `RELIQUARY_GOOGLE_CLIENT_ID` and `RELIQUARY_GOOGLE_CLIENT_SECRET` | The OAuth client. Use the `GITHUB` or `DISCORD` settings for the other services. |
   | `RELIQUARY_DEVELOPMENT_SIGN_IN` | `false` |
   | `RELIQUARY_KEEP_BACKUPS` | The number of local backups, for example `14` |

   [Sign-in services](sign-in-services.md) tells how to make the OAuth client of each service.
   The redirect URI of the OAuth client is `https://your-domain/api/v1/auth/external/<service>/callback`.

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

6. Open `https://your-domain`, and sign in with the account of the administrator email address.
   [Do a check of the sign-in](sign-in-services.md#do-a-check-of-the-sign-in) gives the steps.

## Vault keys

The vault of the linked accounts encrypts the secrets of the plugins, for example the API key of an external site ([ADR 0050](../decisions/0050-no-plugin-code-on-the-server.md)).
The `.env` file gives the vault keys in `RELIQUARY_VAULT_KEY_1`, `RELIQUARY_VAULT_KEY_2` and `RELIQUARY_VAULT_KEY_3`.
`reliquary-update --init` makes the first key.

The key with the highest number encrypts the new secrets.
To change the key:

1. Make a key:

   ```shell
   openssl rand -base64 32
   ```

2. Put the key in the next empty setting, for example `RELIQUARY_VAULT_KEY_2`.
   Do not delete the old key. The vault needs it to read the old secrets.
3. Apply the change:

   ```shell
   ./reliquary-update
   ```

When a plugin reads a secret of an old key, the vault encrypts it again with the new key.
An instance from before the vault has no key. Add `RELIQUARY_VAULT_KEY_1` to its `.env` file.
Without a key, the users cannot link an account with an API key.
When you lose a key, the users must link their accounts again.

## WebSockets for the online games

The online games use a WebSocket on the path `/api/v1/playtest/hub`.
The reverse proxy must let WebSockets through on this path.
A game can stay open for hours, so the connection must stay open for a long time.

- The reverse proxy must send the headers `Upgrade` and `Connection` to the web container.
- The read timeout and the idle timeout of this path must be 60 seconds or more. Use 1 hour, as the web container does.
  The browser and the API send a ping each 15 seconds, so a connection with no message for 30 seconds is lost.
- The reverse proxy must not keep the query string of this path in its access log.
  The query string contains the game token of the player.

With its default settings, Caddy lets WebSockets through and keeps them open.
The access log of Caddy is off by default. When you enable it, it contains the query string.
With Nginx, use the settings of the location `/api/v1/playtest/hub` in `web/nginx.conf.template`.

Two API replicas or more need no sticky sessions.
The browser connects to the hub with no negotiation, and the replicas share the games and the hub messages in Redis.
The [online games architecture](../architecture/online-games.md#table-hub) gives the details.

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
   The backup does not contain the vault keys either.

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
- Do not open the ports of PostgreSQL or Redis. The Compose file does not open them.
- Keep `RELIQUARY_DEVELOPMENT_SIGN_IN` at `false`. With `true`, each visitor can open each account.
- Update to each new release that fixes a security problem. [Security policy](https://github.com/nicolas-goyon/reliquary/blob/develop/SECURITY.md) tells how to report a problem.
