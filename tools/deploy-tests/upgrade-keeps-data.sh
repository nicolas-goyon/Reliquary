#!/bin/sh
# Gate of the milestone "Pre-production deployment": an update keeps the data (UpgradeKeepsData).
#
# Usage: tools/deploy-tests/upgrade-keeps-data.sh OLD_VERSION NEW_VERSION
#
# The test installs OLD_VERSION with the script reliquary-update, creates data, updates to NEW_VERSION,
# and checks the data. Then it rolls back to OLD_VERSION and checks that the data is the data before the update.
# Set RELIQUARY_PULL=false to use local images, and RELIQUARY_IMAGE_PREFIX to use other images.
# OLD_PULL and NEW_PULL replace RELIQUARY_PULL for the installation and for the update, for example
# to pull the old images from the registry and to use the new images of a local build.
# Set UPDATE_SCRIPT=pwsh to test the PowerShell script reliquary-update.ps1 instead of the POSIX script.
# The test uses the ports 8190 and 18135, and the Compose project "reliquary-gate". It deletes the project at the end.

set -eu

old_version=$1
new_version=$2
repository=$(cd "$(dirname "$0")/../.." && pwd)
home=$(mktemp -d)
base=http://localhost:8190
project=reliquary-gate

log() {
    printf 'upgrade-keeps-data: %s\n' "$*"
}

fail() {
    printf 'upgrade-keeps-data: FAIL: %s\n' "$*" >&2
    exit 1
}

# Runs the script under test: reliquary-update, or reliquary-update.ps1 with PowerShell.
update() {
    if [ "${UPDATE_SCRIPT:-sh}" = "pwsh" ]; then
        pwsh -NoProfile -NonInteractive -File "$home/reliquary-update.ps1" "$@"
    else
        "$home/reliquary-update" "$@"
    fi
}

cleanup() {
    status=$?
    if [ "$status" -ne 0 ] && [ -f "$home/.state/current/compose.yaml" ]; then
        docker compose --project-directory "$home" --file "$home/.state/current/compose.yaml" \
            --env-file "$home/.env" --env-file "$home/.state/current/images.env" logs --no-color --tail 100 || true
    fi
    docker compose --project-name "$project" --file "$repository/deploy/compose.yaml" \
        --env-file "$home/.env" down --volumes --remove-orphans >/dev/null 2>&1 || true
    docker run --rm --volume "$home:/home" alpine:3 rm -rf /home/backups >/dev/null 2>&1 || true
    rm -rf "$home"
    exit "$status"
}
trap cleanup EXIT

# Prints the JSON value at a dotted path, for example "account.id".
json() {
    python3 -c 'import json, sys
value = json.load(sys.stdin)
for key in sys.argv[1].split("."):
    value = value[int(key)] if isinstance(value, list) else value[key]
print(value)' "$1"
}

api() {
    method=$1
    path=$2
    shift 2
    curl --fail-with-body --silent --show-error --request "$method" \
        --header "Authorization: Bearer $token" --header 'Content-Type: application/json' "$@" "$base/api/v1$path"
}

sign_in() {
    token=$(curl --fail-with-body --silent --show-error --header 'Content-Type: application/json' \
        --data '{"email":"gate@reliquary.localhost","password":"gate-password-1234"}' \
        "$base/api/v1/auth/sign-in" | json accessToken)
}

check_data() {
    step=$1
    sign_in || fail "$step: the account cannot sign in."
    api GET /decks | grep --quiet '"name":"Gate deck"' || fail "$step: the deck is missing."
    api GET "/decks/$deck_id/versions" | grep --quiet '"message":"First version"' || fail "$step: the deck version is missing."
    api GET "/decks/$deck_id/variants" | grep --quiet '"name":"Gate variant"' || fail "$step: the variant is missing."
    api GET /collection/locations | grep --quiet '"name":"Gate binder"' || fail "$step: the collection location is missing."
    log "$step: the account, the deck, the version, the variant and the location are there."
}

log "Install the script in $home."
cp "$repository/deploy/reliquary-update" "$repository/deploy/reliquary-update.ps1" "$repository/deploy/.env.example" "$home/"
update --init "$old_version"
sed -i.bak \
    -e "s|^RELIQUARY_PROJECT=.*|RELIQUARY_PROJECT=$project|" \
    -e "s|^RELIQUARY_APP_PORT=.*|RELIQUARY_APP_PORT=8190|" \
    -e "s|^RELIQUARY_MAILPIT_PORT=.*|RELIQUARY_MAILPIT_PORT=18135|" \
    -e "s|^RELIQUARY_PUBLIC_URL=.*|RELIQUARY_PUBLIC_URL=$base|" \
    "$home/.env"
# The test checks the script of this commit, so the script must not replace itself with the script of an image.
printf 'RELIQUARY_SELF_UPDATE=false\n' >> "$home/.env"
if [ -n "${RELIQUARY_IMAGE_PREFIX:-}" ]; then
    printf 'RELIQUARY_IMAGE_PREFIX=%s\n' "$RELIQUARY_IMAGE_PREFIX" >> "$home/.env"
fi

log "Install version $old_version."
RELIQUARY_PULL=${OLD_PULL:-${RELIQUARY_PULL:-true}} update "$old_version"

log "Create the data."
curl --fail-with-body --silent --show-error --header 'Content-Type: application/json' \
    --data '{"email":"gate@reliquary.localhost","password":"gate-password-1234","displayName":"Gate","language":"en"}' \
    "$base/api/v1/auth/register" >/dev/null
sign_in
deck_id=$(api POST /decks --data '{"name":"Gate deck","lines":[]}' | json id)
api POST "/decks/$deck_id/versions" --data '{"message":"First version"}' >/dev/null
api POST "/decks/$deck_id/variants" --data '{"name":"Gate variant"}' >/dev/null
api POST /collection/locations --data '{"name":"Gate binder","kind":"binder"}' >/dev/null
check_data "Before the update"

log "Update to version $new_version."
RELIQUARY_PULL=${NEW_PULL:-${RELIQUARY_PULL:-true}} update "$new_version"
check_data "After the update"
api POST /decks --data '{"name":"Deck after the update","lines":[]}' >/dev/null

log "Roll back to the previous version."
update --rollback --yes
check_data "After the rollback"
if api GET /decks | grep --quiet '"name":"Deck after the update"'; then
    fail "After the rollback: the deck made after the update is there. The rollback did not restore the backup before the update."
fi
log "After the rollback: the deck made after the update is not there, as expected."

log "PASS"
