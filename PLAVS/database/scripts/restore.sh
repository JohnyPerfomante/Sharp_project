#!/usr/bin/env sh
set -eu
if [ "$#" -ne 2 ]; then
  printf 'Usage: %s backup.dump NEW_DATABASE_NAME\nRestore into a new empty database only.\n' "$0" >&2
  exit 2
fi
case "$2" in *[!a-zA-Z0-9_]*) printf 'Invalid database name\n' >&2; exit 2;; esac
docker compose exec -T db sh -c 'createdb -U "$POSTGRES_USER" "$1"' sh "$2"
docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" --no-owner -d "$1"' sh "$2" < "$1"
