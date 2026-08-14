#!/usr/bin/env bash
# Stops the project-local PostgreSQL instance started by start-local-postgres.sh.
# Usage: scripts/stop-local-postgres.sh
set -euo pipefail

PORT="${PG_PORT:-5433}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATA_DIR="$ROOT/pgdata/local-dev"

if [[ -z "${PG_BIN:-}" ]]; then
  if command -v pg_ctl >/dev/null 2>&1; then
    PG_BIN="$(dirname "$(command -v pg_ctl)")"
  elif [[ -d "/c/Program Files/PostgreSQL/18/bin" ]]; then
    PG_BIN="/c/Program Files/PostgreSQL/18/bin"
  else
    echo "ERROR: PostgreSQL binaries not found. Set PG_BIN." >&2
    exit 1
  fi
fi

if [[ -f "$DATA_DIR/PG_VERSION" ]]; then
  "$PG_BIN/pg_ctl.exe" -D "$DATA_DIR" stop >/dev/null 2>&1 || true
fi

echo "PostgreSQL on port $PORT stopped (data preserved in $DATA_DIR)."
