# Local instance from the source code

A local instance runs the code of your working tree. Use it to develop Reliquary and to test a branch.
It contains demo data with public passwords, so do not open it to other computers.
Terms follow the [glossary](../glossary.md).

To use Reliquary every day with your own data, use a [pre-production instance](pre-production.md).

## Requirements

- Docker with the Compose plugin, Git and `just`. [Install the software](requirements.md#install-the-software) tells how to install them.
- For hot reload only: the .NET SDK 10.0 and Node.js 24. See [Develop with hot reload](#develop-with-hot-reload).

## Install

1. Open a terminal:

   /// tab | Linux

   Open a terminal of your desktop.

   ///

   /// tab | Windows

   Open **Ubuntu** in the Start menu.
   The commands put the repository in the Linux file system, in `~/reliquary`.
   Do not clone it in `/mnt/c`: the build is much slower there.

   With Git Bash, add `export MSYS_NO_PATHCONV=1` to `~/.bashrc` first. [Git Bash](requirements.md#git-bash) tells why.

   ///

2. Clone the repository and open its folder:

   ```shell
   git clone https://github.com/nicolas-goyon/reliquary.git
   cd reliquary
   ```

3. Copy `.env.example` to `.env`. Change the ports if other programs use them:

   ```shell
   cp .env.example .env
   ```

4. Run the command:

   ```shell
   just up
   ```

   The command builds the images, applies the migrations, loads the demo data and starts the instance.
   The first run takes some minutes.

5. Open <http://localhost:8080> in a browser. On Windows, use the browser of Windows. Docker Desktop sends the port to Windows.

The demo accounts are:

| Email address | Password | Role |
| --- | --- | --- |
| `demo@reliquary.localhost` | `reliquary-demo` | User, English interface |
| `demo-fr@reliquary.localhost` | `reliquary-demo` | User, French interface |
| `admin@reliquary.localhost` | `reliquary-admin` | Administrator |

## Develop with hot reload

Install the .NET SDK 10.0 and Node.js 24 first:

/// tab | Linux

1. Install the .NET SDK with the script of Microsoft, then add it to the variable `PATH`:

   ```shell
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
   echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
   ```

   For the packages of your distribution, read the [instructions of Microsoft](https://learn.microsoft.com/dotnet/core/install/linux).

2. Install Node.js with [nvm](https://github.com/nvm-sh/nvm). In the folder `web`, run `nvm install`. The command reads the version in `web/.nvmrc`.
3. Open a new terminal, and check the versions with `dotnet --version` and `node --version`.

///

/// tab | Windows

Install the tools in WSL, not in Windows. The commands of `just` run in the Ubuntu terminal.

1. In the Ubuntu terminal, install the .NET SDK with the script of Microsoft, then add it to the variable `PATH`:

   ```shell
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
   echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
   ```

2. Install Node.js with [nvm](https://github.com/nvm-sh/nvm). In the folder `web`, run `nvm install`. The command reads the version in `web/.nvmrc`.
3. Open a new Ubuntu terminal, and check the versions with `dotnet --version` and `node --version`.
4. To edit the code, install [Visual Studio Code](https://code.visualstudio.com) in Windows with the extension **WSL**.
   In the folder of the repository, run `code .`. The editor then uses the tools of WSL.

The browser of Windows opens <http://localhost:5080> and <http://localhost:5173>, because WSL sends the ports to Windows.

///

Then start the instance:

1. Run `just infra` to start PostgreSQL, Redis and Mailpit only.
2. Run `just restore` to install the .NET packages and the npm packages.
3. Run `just migrate`, then `just demo-local`.
4. Run `just api` (API on <http://localhost:5080>).
5. In a second terminal, run `just web` (web app on <http://localhost:5173>).

[Getting started](../development/getting-started.md) gives the code layout and the other commands.

## Update

To test a different commit or branch:

1. Get the code:

   ```shell
   git pull
   ```

2. Run `just up` again. The command builds the new images and applies the new migrations. The data stays.

Some changes of a branch need an empty database, for example a migration that changed during the review of a pull request.
Then run `just reset`. The command deletes the data and creates the instance again.

## Keep data between tests

- `just backup NAME` saves the databases to `backups/NAME`.
- `just restore-backup NAME` puts them back.
- `just full-cards` imports all the cards instead of the 342 demo cards.

To keep your accounts and your imports through all the tests, use a [pre-production instance](pre-production.md) next to the local instance.

## Commands

| Command | Effect |
| --- | --- |
| `just up` | Create or update the instance |
| `just stop` | Stop the instance. The data stays. |
| `just reset` | Delete the data, then create the instance again |
| `just down` | Delete the instance and its data |
| `just ps` | Show the state of the containers |

Several local instances can run at the same time. Give each instance a name (`RELIQUARY_INSTANCE`) and different ports in its `.env` file.
The name `preprod` is for the pre-production instance.
