# Requirements

This page gives what each mode needs.
Terms follow the [glossary](../glossary.md).

## Software

| Software | Local instance | Pre-production instance | Production instance |
| --- | --- | --- | --- |
| Docker with the Compose plugin 2.17 or later | Yes | Yes | Yes (Docker Compose) |
| A POSIX shell (Linux, macOS, or WSL on Windows) | Yes | Yes | Yes |
| [just](https://just.systems) 1.40 or later | Yes | Optional | No |
| Git | Yes | Optional | No |
| .NET SDK 10.0 (see `global.json`) | Only for hot reload | No | No |
| Node.js 24, or 22.12 and later | Only for hot reload | No | No |
| Python 3.11 or later | Only for the documentation checks | No | No |
| A reverse proxy with HTTPS, for example Caddy | No | No | Yes |

On Windows, run all the commands in WSL 2 (Windows Subsystem for Linux). [Install the software](#install-the-software) tells how.

## Install the software

Select your system. The other pages of the site then show the same system.

/// tab | Linux

These steps are for Debian and Ubuntu. For a different distribution, use the instructions of Docker for that distribution.

1. Install Docker Engine and the Compose plugin with the [instructions of Docker](https://docs.docker.com/engine/install/).
2. Give your user the permission to use Docker, then sign out and sign in again:

   ```shell
   sudo usermod --append --groups docker "$USER"
   ```

3. Start Docker now and each time the computer starts:

   ```shell
   sudo systemctl enable --now docker
   ```

4. Check the version of Compose. It must be 2.17 or later:

   ```shell
   docker compose version
   ```

5. For a local instance only, install Git and `just`:

   ```shell
   sudo apt update && sudo apt install --yes git curl
   mkdir -p ~/.local/bin
   curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh | bash -s -- --to ~/.local/bin
   ```

   When the command `just` is not found, add `~/.local/bin` to the variable `PATH`, or open a new terminal.

///

/// tab | Windows

These steps are for Windows 10 version 22H2 and Windows 11.
They install WSL 2 with Ubuntu, and Docker Desktop.

1. Open PowerShell as an administrator, and install WSL 2 with Ubuntu:

   ```powershell
   wsl --install
   ```

2. Restart the computer. Open **Ubuntu** in the Start menu, and make a Linux user name and a password.
3. In PowerShell, install Docker Desktop:

   ```powershell
   winget install --exact --id Docker.DockerDesktop
   ```

4. Start Docker Desktop. In **Settings**, set these options:
   - **General**: select **Use the WSL 2 based engine** and **Start Docker Desktop when you sign in to your computer**.
   - **Resources → WSL integration**: enable **Ubuntu**.
5. In the Ubuntu terminal, check the version of Compose. It must be 2.17 or later:

   ```shell
   docker compose version
   ```

6. For a local instance only, install Git and `just` in the Ubuntu terminal:

   ```shell
   sudo apt update && sudo apt install --yes git curl
   mkdir -p ~/.local/bin
   curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh | bash -s -- --to ~/.local/bin
   ```

   When the command `just` is not found, open a new Ubuntu terminal.

Run all the commands of this site in the Ubuntu terminal, not in PowerShell.
Keep the folders of Reliquary in the Linux file system, for example in `~`, and not in `/mnt/c`.
The Windows file system is slower in WSL, and it does not keep the Linux permissions of the backups.
To open the Linux folders in the File Explorer, type `\\wsl.localhost\Ubuntu\home` in the address bar.

///

## Hardware

| Item | Local instance | Pre-production or production instance |
| --- | --- | --- |
| Processor | 64-bit x86 or ARM | 64-bit x86 or ARM |
| Memory | 2 GB free | 2 GB free, 4 GB during the card import |
| Disk | 2 GB with the demo data | 4 GB for all the cards, plus the backups |

The published images exist for `linux/amd64` and `linux/arm64`.
Each backup contains all the databases. With all the cards, a backup can need 1 GB or more.

## Network

| Address | Local instance | Pre-production instance | Production instance |
| --- | --- | --- | --- |
| Web app and API | <http://localhost:8080> | <http://localhost:8090> | The address of your reverse proxy |
| Mailpit (test emails) | <http://localhost:18025> | <http://localhost:18035> | Not used with a real email server |
| PostgreSQL | `localhost:15432` | Not open | Not open |
| Redis | `localhost:16379` | Not open | Not open |

The `.env` file of each instance can change the ports.

An instance needs these outbound connections:

- `ghcr.io`, to pull the published images (pre-production and production instances).
- `api.scryfall.com`, `data.scryfall.io`, `mtgjson.com` and `json.commanderspellbook.com`, to import the card data.
- The email server, when you use a real email server.
