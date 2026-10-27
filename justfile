# Commands for local development. Run "just" to list them.
# Requirements: just, Docker with Compose, the .NET SDK, Node.js and Python 3.

set dotenv-load := true

instance := env("RELIQUARY_INSTANCE", "dev")
compose := "docker compose --project-name reliquary-" + instance + " --file compose.yaml"

# List the recipes.
default:
    @just --list

# Create or update a full local instance on http://localhost:8080: build the images, apply the migrations and load the demo data.
up:
    {{ compose }} --profile app build
    {{ compose }} up --detach --wait postgres redis mailpit
    {{ compose }} --profile app run --rm migrate migrate
    {{ compose }} --profile app run --rm migrate demo load
    {{ compose }} --profile app up --detach --wait api worker web

# Start only the local infrastructure (PostgreSQL, Redis and Mailpit), for "just api" and "just web".
infra:
    {{ compose }} up --detach --wait postgres redis mailpit

# Stop the local instance. The data stays.
stop:
    {{ compose }} --profile app --profile observability stop

# Delete the data of the local instance, then create it again.
reset:
    {{ compose }} --profile app --profile observability down --volumes --remove-orphans
    just up

# Destroy the local instance and its data.
down:
    {{ compose }} --profile app --profile observability down --volumes --remove-orphans

# Back up all the databases of the local instance to the folder backups/NAME. See docs/development/backup-and-restore.md.
backup name:
    mkdir -p backups && chmod a+rwx backups
    {{ compose }} --profile app run --rm --volume "{{ justfile_directory() }}/backups:/backups" migrate backup /backups/{{ name }}

# Restore the backup backups/NAME into the local instance. It stops the API and the worker, and replaces the data.
restore-backup name:
    {{ compose }} up --detach --wait postgres redis mailpit
    {{ compose }} --profile app stop api worker
    {{ compose }} --profile app run --rm --volume "{{ justfile_directory() }}/backups:/backups" migrate restore /backups/{{ name }} --replace
    {{ compose }} --profile app run --rm migrate migrate
    {{ compose }} --profile app up --detach --wait api worker web

# Load the demo dataset into the local instance: the demo cards and the demo accounts.
demo:
    {{ compose }} --profile app run --rm migrate demo load

# Start Grafana and the OpenTelemetry collector of the local instance on http://localhost:3000.
observability:
    {{ compose }} --profile observability up --detach observability

# Show the state of the local instance.
ps:
    {{ compose }} --profile app ps

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

# Load the demo dataset into the local databases, for "just api".
demo-local:
    Cards__Import__Directory={{ justfile_directory() }}/data/demo dotnet run --project src/Reliquary.Cli -- demo load

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
