#!/usr/bin/env bash
# Runs pg_dump or pg_restore in the PostgreSQL container that PG_CONTAINER names.
# CI uses this script for the backup tests, so the runner does not install the PostgreSQL client.
# The tools in the container have the same version as the server.
#
# Usage: make a link with the name of the tool (pg_dump or pg_restore) to this script, and put the link in the PATH.
#
# The script changes the arguments of InstanceBackup (src/Reliquary.Platform/Backup/InstanceBackup.cs):
# - It removes --host and --port. The tool connects to the server of the container through the local socket.
# - pg_dump: the tool writes the file of --file=PATH in the container. The script then copies it to PATH on the runner.
# - pg_restore: the script copies the file of the last argument to the container. The tool then reads it there.
set -euo pipefail

tool=$(basename "$0")
container=${PG_CONTAINER:?Set PG_CONTAINER to the ID of the PostgreSQL container.}
work="/tmp/reliquary-ci-$$-$RANDOM"
arguments=()
output=""
input=""
for argument in "$@"; do
  case "$argument" in
    --host=* | --port=*) ;;
    --file=*) output="${argument#--file=}" ;;
    -*) arguments+=("$argument") ;;
    *) input="$argument" ;;
  esac
done

cleanup() { docker exec "$container" rm -f "$work" > /dev/null 2>&1 || true; }
trap cleanup EXIT

if [ -n "$output" ]; then
  docker exec --env PGPASSWORD "$container" "$tool" "${arguments[@]}" "--file=$work"
  docker cp --quiet "$container:$work" "$output"
elif [ -n "$input" ]; then
  docker cp --quiet "$input" "$container:$work"
  docker exec --env PGPASSWORD "$container" "$tool" "${arguments[@]}" "$work"
else
  docker exec --env PGPASSWORD "$container" "$tool" "${arguments[@]}"
fi
