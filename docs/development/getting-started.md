# Getting started

This guide tells how to run Reliquary on your computer. It also gives the layout of the code.
Terms follow the [glossary](../glossary.md).

## Requirements

| Tool | Version | Use |
| --- | --- | --- |
| .NET SDK | 10.0 (see `global.json`) | Backend |
| Node.js | 24 (see `web/.nvmrc`), or 22.12 and later | Web app |
| Docker with Compose | Compose 2.17 or later | PostgreSQL and Redis |
| [just](https://just.systems) | 1.40 or later | Development commands |
| Python | 3.11 or later | Check of the writing rules |

On Windows, run the `just` recipes in Git Bash or in WSL.

## Try a full local instance

You need Docker with Compose and `just` only.

1. Clone the repository and open its folder.
2. Copy `.env.example` to `.env`. Change the ports if other programs use them.
3. Run `just up`.
   The command builds the images, applies the migrations, loads the demo data and starts the instance.
4. Open <http://localhost:8080> in a browser.

The instance contains the demo cards of `data/demo` and these demo accounts:

| Email address | Password | Role |
| --- | --- | --- |
| `demo@reliquary.localhost` | `reliquary-demo` | User, English interface |
| `demo-fr@reliquary.localhost` | `reliquary-demo` | User, French interface |
| `admin@reliquary.localhost` | `reliquary-admin` | Administrator |

The passwords are public. Do not open a local instance to other computers.

## Start Reliquary for development

1. Clone the repository and open its folder.
2. Copy `.env.example` to `.env`. Change the ports if other programs use them.
3. Run `just infra` to start PostgreSQL, Redis and Mailpit.
4. Run `just restore` to install the .NET packages and the npm packages.
5. Run `just api` to start the API on <http://localhost:5080>.
6. In a second terminal, run `just web` to start the web app on <http://localhost:5173>.
7. Open <http://localhost:5173> in a browser.

The page shows "The API is available" and the version of the API.
The language list at the top of the page changes the interface language.

To make an account:

1. Select **Make an account**, then complete the form.
2. Open Mailpit at <http://localhost:18025> and open the email of Reliquary.
3. Open the link in the email, then sign in.

To load the demo cards and the demo accounts into the development databases, run `just demo-local`.
The account `admin@reliquary.localhost` is an administrator of the local instance.
[Accounts and authentication](../architecture/accounts.md) gives the settings of the OAuth providers.

The API also serves these addresses:

| Address | Content |
| --- | --- |
| <http://localhost:5080/health/live> | Health check of the API process |
| <http://localhost:5080/health/ready> | Health check of PostgreSQL and Redis |
| <http://localhost:5080/metrics> | Metrics in the Prometheus text format |
| <http://localhost:5080/api/v1/status> | Name, version and core components of the API |
| <http://localhost:5080/openapi/v1.json> | OpenAPI document of the public API |
| <http://localhost:18025> | Mailpit: the emails of the local instance |

## Manage the local instance

| Command | Effect |
| --- | --- |
| `just up` | Create or update the full instance: build the images, apply the migrations, load the demo data and start the services |
| `just infra` | Start only PostgreSQL, Redis and Mailpit |
| `just demo` | Load the demo data into the full instance again |
| `just stop` | Stop the instance. The data stays. |
| `just reset` | Delete the data, then create the instance again |
| `just down` | Destroy the instance: no container and no volume stays |
| `just ps` | Show the state of the containers |

The full instance contains these services:

| Service | Content |
| --- | --- |
| `postgres` | The databases |
| `redis` | The cache, the quota counters and the data protection keys |
| `mailpit` | A test email server. It keeps the emails and sends none. |
| `migrate` | The CLI. `just up` runs it to apply the migrations and load the demo data. |
| `api` | The public API |
| `worker` | The outbox publisher and the scheduled jobs |
| `web` | The web app. Nginx sends the `/api` requests to the API. |

To use the full card data, set `RELIQUARY_CARD_DATA` to a folder with the files of [the card data guide](card-data.md).

## Run several instances at the same time

Each instance needs a name and its own ports.
Give the values on the command line, or in a second copy of the repository with its own `.env` file.

```shell
export RELIQUARY_INSTANCE=plugin-test RELIQUARY_APP_PORT=8081 RELIQUARY_POSTGRES_PORT=15433 \
  RELIQUARY_REDIS_PORT=16380 RELIQUARY_SMTP_PORT=11026 RELIQUARY_MAILPIT_PORT=18026
just up
just down
```

The instances share the images, but each instance has its own containers and volumes.

## Check your work

| Command | Effect |
| --- | --- |
| `just build` | Build the backend and the web app |
| `just test` | Run the .NET tests and the web tests |
| `just lint` | Run all the checks of the code and the documentation |
| `just format` | Correct the format of the .NET code |

CI runs the same checks for each pull request.

## Code layout

| Path | Content |
| --- | --- |
| `Reliquary.slnx` | The .NET solution |
| `Directory.Build.props` | Build settings for all .NET projects |
| `Directory.Packages.props` | Versions of the NuGet packages |
| `src/Reliquary.Api` | Host of the public API |
| `src/Reliquary.Worker` | Host of the background work: the outbox publisher and the scheduled jobs |
| `src/Reliquary.Cli` | The command line tool `reliquary`, for example `reliquary migrate` |
| `src/Reliquary.Hosting` | Registers the platform and the core components in the three hosts |
| `src/Reliquary.Platform` | Shared infrastructure: connections, shard directory, migrations, outbox and scheduled jobs |
| `src/Reliquary.BuildingBlocks` | Shared contracts, for example `ICoreComponent`, `IDatabase` and `IDomainEventWriter` |
| `src/Components/Reliquary.Accounts` | Accounts, authentication, API keys and quotas |
| `src/Components/Reliquary.Cards` | Card reference data, card search and deck analysis |
| `src/Components/Reliquary.Decks` | Decks, deck versions, variants, tags and folders |
| `src/Components/Reliquary.Collection` | Locations, location entries, movements and inventories |
| `src/Components/Reliquary.Playtest` | Table engine and game sessions |
| `tests/Reliquary.Api.Tests` | Tests of the API host |
| `tests/Reliquary.Architecture.Tests` | Tests of the rules between the core components |
| `tests/Reliquary.Platform.Tests` | Integration tests of the platform, on PostgreSQL |
| `tests/Reliquary.Cards.Tests` | Tests of the card import, the card endpoints and the card search, with real card fixtures |
| `bench/Reliquary.Cards.Benchmark` | The benchmark of the card search |
| `data/demo` | The demo card dataset, for the parity tests of the card search |
| `tests/Reliquary.BuildingBlocks.Tests` | Unit tests of the shared contracts |
| `tools/demo-data` | The script that creates small card datasets |
| `tools/search-parity` | The queries and the scripts of the parity tests of the card search |
| `tests/Shared` | Helpers that the test projects share, for example the test databases |
| `web` | The web app |
| `tools/ste-lint` | Check of the writing rules |
| `docs` | Documentation |

## Rules for the core components

- A core component references `Reliquary.BuildingBlocks` only. It never references `Reliquary.Platform` or `Reliquary.Hosting`.
- A core component never references another core component. The architecture tests fail if it does.
- A core component communicates with other core components through interfaces and domain events.
- Each core component has one class that implements `ICoreComponent`. `src/Reliquary.Hosting/CoreComponents.cs` lists these classes.
- A core component declares its SQL scripts as migration sets. The [database](../architecture/database.md) page gives the rules.

The [architecture overview](../architecture/overview.md) and [ADR 0007](../decisions/0007-modular-monolith.md) give the reasons.

## Add a core component

1. Create the project `src/Components/Reliquary.<Name>/Reliquary.<Name>.csproj`. Copy the content from an existing core component.
2. Add a class `<Name>Component` that implements `ICoreComponent`.
3. Add the project to `Reliquary.slnx`, to `src/Reliquary.Hosting/Reliquary.Hosting.csproj` and to `tests/Reliquary.Architecture.Tests/Reliquary.Architecture.Tests.csproj`.
4. Add the component to `CoreComponents.All` in `src/Reliquary.Hosting/CoreComponents.cs`.
5. Add the assembly to `ComponentAssemblies` in `CoreComponentTests.cs`.
6. Update the expected list of components in `ApiTests.cs`.
7. Record the new core component in an ADR.
