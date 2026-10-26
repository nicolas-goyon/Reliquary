# Commands for local development. Run "just" to list them.
# Requirements: just, Docker with Compose, the .NET SDK, Node.js and Python 3.

set dotenv-load := true

instance := env("RELIQUARY_INSTANCE", "dev")
compose := "docker compose --project-name reliquary-" + instance + " --file compose.yaml"

# List the recipes.
default:
    @just --list

# Create or update the local infrastructure (PostgreSQL and Redis).
up:
    {{ compose }} up --detach --wait

# Stop the local infrastructure. The data stays.
stop:
    {{ compose }} stop

# Delete the data of the local infrastructure, then create it again.
reset:
    {{ compose }} down --volumes
    {{ compose }} up --detach --wait

# Destroy the local infrastructure and its data.
down:
    {{ compose }} down --volumes --remove-orphans

# Show the state of the local infrastructure.
ps:
    {{ compose }} ps

# Install the .NET packages and the npm packages.
restore:
    dotnet restore
    npm --prefix web ci

# Build the backend and the web app.
build:
    dotnet build
    npm --prefix web run build

# Run all the tests.
test:
    dotnet test
    npm --prefix web test

# Start the API with hot reload on http://localhost:5080.
api:
    dotnet watch --project src/Reliquary.Api

# Start the web app with hot reload on http://localhost:5173.
web:
    npm --prefix web run dev

# Start the worker: the outbox publisher and the scheduled jobs.
worker:
    dotnet run --project src/Reliquary.Worker

# Run a command of the CLI, for example "just cli migrate --check".
cli *args:
    dotnet run --project src/Reliquary.Cli -- {{ args }}

# Apply the database migrations to the local databases.
migrate:
    dotnet run --project src/Reliquary.Cli -- migrate

# Check the code and the documentation.
lint: lint-code lint-docs

# Check the format and the style of the code.
lint-code:
    dotnet format --verify-no-changes
    npm --prefix web run lint
    npm --prefix web run typecheck

# Check the documentation against the ASD-STE100 rules and the Markdown rules.
lint-docs:
    python3 tools/ste-lint/lint-docs.py
    npx --yes markdownlint-cli2@0.24.0

# Correct the format of the .NET code.
format:
    dotnet format
