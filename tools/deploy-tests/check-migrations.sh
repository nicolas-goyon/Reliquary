#!/bin/sh
# Fails when the changes since BASE modify, rename or delete a migration file that BASE contains.
# A deployed instance already applied these files, and the migration runner records only the name of a script.
# See docs/decisions/0042-deployed-instances-and-updates.md.
#
# Usage: tools/deploy-tests/check-migrations.sh BASE   (for example origin/develop)

set -eu

base=${1:?Give the base, for example origin/develop.}
merge_base=$(git merge-base "$base" HEAD)

changes=$(git diff --name-status --find-renames "$merge_base" HEAD -- 'src/**/Migrations/**' |
    awk '$1 != "A" { print }')

if [ -n "$changes" ]; then
    echo "check-migrations: these changes modify, rename or delete a migration that $base contains:"
    echo "$changes"
    echo "check-migrations: add a new migration file instead. See docs/decisions/0042-deployed-instances-and-updates.md."
    exit 1
fi

echo "check-migrations: no existing migration changed since $base."
