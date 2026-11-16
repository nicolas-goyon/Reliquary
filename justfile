# Commands for local development. Run "just" to list them.
# Requirements: just, Docker with Compose, the .NET SDK, Node.js and Python 3.

set dotenv-load := true

instance := env("RELIQUARY_INSTANCE", "dev")
_instance_check := if instance == "preprod" { error("The instance name \"preprod\" is for the deployed instance. Use a different RELIQUARY_INSTANCE.") } else { "" }
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

# Import all the cards from Scryfall into the local instance. Set RELIQUARY_CARD_IMPORT_DIRECTORY to nothing in .env to keep them up to date.
full-cards:
    {{ compose }} --profile app run --rm --env Cards__Import__Directory= migrate jobs run cards.import --force

# Start Grafana and the OpenTelemetry collector of the local instance on http://localhost:3000.
observability:
    {{ compose }} --profile observability up --detach observability

# Show the state of the local instance.
ps:
    {{ compose }} --profile app ps

# The folder of the pre-production instance. See https://nicolas-goyon.github.io/reliquary/deployment/pre-production/.
preprod_home := env("RELIQUARY_PREPROD_HOME", home_directory() / "reliquary-preprod")

# Install the script of the pre-production instance and make its .env file with new secrets. VERSION is "develop", "latest" or a release number.
preprod-install version="develop":
    mkdir -p "{{ preprod_home }}"
    cp deploy/reliquary-update deploy/.env.example "{{ preprod_home }}/"
    "{{ preprod_home }}/reliquary-update" --init {{ version }}

# Update the pre-production instance to VERSION ("develop", "latest" or a release number), with a backup before the update.
preprod-update *version:
    "{{ preprod_home }}/reliquary-update" {{ version }}

# Start the previous version of the pre-production instance again, with the data of the backup before the last update.
preprod-rollback:
    "{{ preprod_home }}/reliquary-update" --rollback

# Show the version and the containers of the pre-production instance.
preprod-status:
    "{{ preprod_home }}/reliquary-update" --status

# Install the .NET packages and the npm packages.
restore:
    dotnet restore
    npm --prefix web ci
    npm --prefix sdk ci

# Build the backend and the web app.
build:
    dotnet build
    npm --prefix web run build
    npm --prefix sdk run build

# Run all the tests.
test:
    dotnet test
    npm --prefix web test
    npm --prefix sdk test

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

# The instance of the end-to-end test: compose.yaml and the overlay e2e/compose.yaml, with two API replicas.
e2e_compose := "docker compose --project-name reliquary-e2e --file compose.yaml --file e2e/compose.yaml"

# Create the instance of the end-to-end test on http://localhost:8080: two API replicas behind the web container. See docs/development/end-to-end-test.md.
e2e-up:
    {{ e2e_compose }} --profile app build
    {{ e2e_compose }} up --detach --wait postgres redis mailpit
    {{ e2e_compose }} --profile app run --rm migrate migrate
    {{ e2e_compose }} --profile app run --rm migrate demo load
    {{ e2e_compose }} --profile app up --detach --wait api worker web
    for _ in $(seq 60); do curl --fail --silent "http://localhost:${RELIQUARY_APP_PORT:-8080}/api/v1/cards/named?name=Sol%20Ring" > /dev/null && exit 0; sleep 5; done; echo "The worker did not import the demo cards in 5 minutes."; exit 1

# Run the end-to-end test of the online games for MINUTES minutes on the instance of "just e2e-up". Install Chromium first: "cd e2e && npm ci && npx playwright install chromium".
e2e minutes="10":
    #!/usr/bin/env bash
    set -euo pipefail
    urls=""
    for index in 1 2; do
        urls="$urls${urls:+,}http://$({{ e2e_compose }} port --index "$index" api 8080)/metrics"
    done
    npm --prefix e2e ci
    cd e2e
    E2E_BASE_URL="http://localhost:${RELIQUARY_APP_PORT:-8080}" E2E_MINUTES={{ minutes }} E2E_METRICS_URLS="$urls" npx playwright test

# Destroy the instance of the end-to-end test and its data.
e2e-down:
    {{ e2e_compose }} --profile app down --volumes --remove-orphans

# Check the code and the documentation.
lint: lint-code lint-docs

# Check the format and the style of the code.
lint-code:
    dotnet format --verify-no-changes
    npm --prefix web run lint
    npm --prefix web run typecheck
    npm --prefix sdk run typecheck

# Check the documentation against the ASD-STE100 rules and the Markdown rules.
lint-docs:
    python3 tools/ste-lint/lint-docs.py
    npx --yes markdownlint-cli2@0.24.0 "**/*.md"

# Check the Helm chart with "helm lint", "helm template" and kubeconform. The tools run in containers.
helm-check:
    tools/deploy-tests/check-chart.sh

# Install the Helm chart on a kind cluster with the images OLD, upgrade to the images NEW, and check the instance. Needs kind, kubectl and helm.
helm-smoke old="latest" new="develop":
    tools/deploy-tests/helm-smoke-test.sh {{ old }} {{ new }}

# Open the pull request that merges develop into main. Then release-please prepares the release. See docs/decisions/0043-releases-with-release-please.md.
release-start:
    gh pr create --base main --head develop --title "chore(release): merge develop into main" --body "Merge with \"Create a merge commit\". Then release-please opens the release pull request on main."

# Build the documentation site in the folder site/, then serve it on http://localhost:8000. Needs Python 3.
docs-site:
    python3 -m venv .venv-docs
    .venv-docs/bin/pip install --quiet -r tools/docs-site/requirements.txt
    NO_MKDOCS_2_WARNING=1 .venv-docs/bin/mkdocs serve

# Correct the format of the .NET code.
format:
    dotnet format
