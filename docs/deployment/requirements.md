# Requirements

This page gives what each mode needs.
Terms follow the [glossary](../glossary.md).

## Software

| Software | Local instance | Pre-production instance | Production instance |
| --- | --- | --- | --- |
| Docker with the Compose plugin 2.17 or later | Yes | Yes | Yes (Docker Compose) |
| A POSIX shell (Linux, macOS, WSL 2 on Windows) | Yes | Yes, or PowerShell on Windows | Yes |
| [just](https://just.systems) 1.40 or later | Yes | Optional | No |
| Git | Yes | Optional | No |
| .NET SDK 10.0 (see `global.json`) | Only for hot reload | No | No |
| Node.js 24, or 22.12 and later | Only for hot reload | No | No |
| Python 3.11 or later | Only for the documentation checks | No | No |
| A reverse proxy with HTTPS, for example Caddy | No | No | Yes |

On Windows, the pre-production instance needs only Docker Desktop and PowerShell. The local instance needs WSL 2 (Windows Subsystem for Linux) or Git Bash. [Install the software](#install-the-software) tells how.

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

1. Open PowerShell, and install Docker Desktop:

   ```powershell
   winget install --exact --id Docker.DockerDesktop
   ```

   Docker Desktop installs WSL 2 when it is necessary. Restart the computer when the installer asks.

2. Start Docker Desktop. In **Settings → General**, select **Use the WSL 2 based engine** and **Start Docker Desktop when you sign in to your computer**.
3. In PowerShell, check the version of Compose. It must be 2.17 or later:

   ```powershell
   docker compose version
   ```

4. Let PowerShell run the scripts of your user. Windows blocks all the scripts by default:

   ```powershell
   Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
   ```

Then select a terminal for each mode:

| Terminal | Pre-production or production instance | Local instance |
| --- | --- | --- |
| PowerShell | Yes, with the script `reliquary-update.ps1` | No: `just` needs a POSIX shell |
| Ubuntu in WSL 2 | Yes, with the script `reliquary-update` | Yes. Use this terminal for development. |
| Git Bash | No. See [Git Bash](#git-bash). | Yes, with the setting `MSYS_NO_PATHCONV=1` |

### Ubuntu in WSL 2

For a local instance, install Ubuntu, Git and `just`:

1. In PowerShell as an administrator, install Ubuntu, then restart the computer:

   ```powershell
   wsl --install --distribution Ubuntu
   ```

2. Open **Ubuntu** in the Start menu, and make a Linux user name and a password.
3. In Docker Desktop, open **Settings → Resources → WSL integration**, and enable **Ubuntu**.
4. In the Ubuntu terminal, install Git and `just`:

   ```shell
   sudo apt update && sudo apt install --yes git curl
   mkdir -p ~/.local/bin
   curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh | bash -s -- --to ~/.local/bin
   ```

   When the command `just` is not found, open a new Ubuntu terminal.

Keep the repository in the Linux file system, for example in `~/reliquary`, and not in `/mnt/c`.
The Windows file system is slower in WSL, and it does not keep the Linux permissions.
To open the Linux folders in the File Explorer, type `\\wsl.localhost\Ubuntu\home` in the address bar.

### Git Bash

Git Bash comes with [Git for Windows](https://gitforwindows.org).
Before it starts a Windows program, Git Bash changes each argument that looks like a Linux path to a Windows path.
Thus `docker run ... /app/deploy/reliquary-update` gives `cat: 'C:/Program Files/Git/app/deploy/reliquary-update': No such file or directory`.

- For a local instance, stop these changes. Add this line to the file `~/.bashrc`, then open a new Git Bash terminal:

  ```shell
  export MSYS_NO_PATHCONV=1
  ```

  Install `just` with `winget install --exact --id Casey.Just`.
- Do not use the script `reliquary-update` in Git Bash. It gives Docker the paths of the containers and the paths of the folder of the instance. Git Bash cannot convert the second type and keep the first type. Use `reliquary-update.ps1` in PowerShell.

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
