#!/usr/bin/env sh
set -eu
mkdir -p artifacts/backups
target="artifacts/backups/plavs-$(date -u +%Y%m%dT%H%M%SZ).dump"
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$target"
printf 'Backup saved: %s\n' "$target"
