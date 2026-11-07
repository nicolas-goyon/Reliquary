# Local instance from the source code

A local instance runs the code of your working tree. Use it to develop Reliquary and to test a branch.
It contains demo data with public passwords, so do not open it to other computers.
Terms follow the [glossary](../glossary.md).

To use Reliquary every day with your own data, use a [pre-production instance](pre-production.md).

## Requirements

- Docker with the Compose plugin, Git and `just`. See [Requirements](requirements.md).
- For hot reload only: the .NET SDK 10.0 and Node.js 24.

## Install

1. Clone the repository and open its folder:

   ```shell
   git clone https://github.com/nicolas-goyon/reliquary.git
   cd reliquary
   ```

2. Copy `.env.example` to `.env`. Change the ports if other programs use them.
3. Run the command:

   ```shell
   just up
   ```

   The command builds the images, applies the migrations, loads the demo data and starts the instance.
   The first run takes some minutes.

4. Open <http://localhost:8080>.

The demo accounts are:

| Email address | Password | Role |
| --- | --- | --- |
| `demo@reliquary.localhost` | `reliquary-demo` | User, English interface |
| `demo-fr@reliquary.localhost` | `reliquary-demo` | User, French interface |
| `admin@reliquary.localhost` | `reliquary-admin` | Administrator |

## Develop with hot reload

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
