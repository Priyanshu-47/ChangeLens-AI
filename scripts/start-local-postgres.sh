#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Starts a project-local PostgreSQL instance (no Docker required).
#
# Uses the PostgreSQL binaries already installed on the machine. Data lives in
# ./pgdata/local-dev (git-ignored). Trust auth on 127.0.0.1 only — dev use.
#
# Usage:
#   scripts/start-local-postgres.sh
#
# Env overrides:
#   PG_BIN   path to PostgreSQL bin dir (default: auto-detect common installs)
#   PG_PORT  port to listen on        (default: 5433 — avoids clashing with a
#                                     system PostgreSQL on 5432)
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

PORT="${PG_PORT:-5433}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATA_DIR="$ROOT/pgdata/local-dev"

if [[ -z "${PG_BIN:-}" ]]; then
  if command -v psql >/dev/null 2>&1; then
    PG_BIN="$(dirname "$(command -v psql)")"
  elif [[ -d "/c/Program Files/PostgreSQL/18/bin" ]]; then
    PG_BIN="/c/Program Files/PostgreSQL/18/bin"
  elif [[ -d "/usr/lib/postgresql/17/bin" ]]; then
    PG_BIN="/usr/lib/postgresql/17/bin"
  else
    echo "ERROR: PostgreSQL binaries not found. Set PG_BIN." >&2
    exit 1
  fi
fi

PSQL() { "$PG_BIN/psql.exe" -h 127.0.0.1 -p "$PORT" -U changelens -d postgres -tAc "$1"; }

if [[ ! -f "$DATA_DIR/PG_VERSION" ]]; then
  echo "==> Initializing data directory at $DATA_DIR"
  mkdir -p "$(dirname "$DATA_DIR")"
  "$PG_BIN/initdb.exe" -D "$DATA_DIR" -U changelens --auth=trust -E UTF8 >/dev/null
fi

if ! "$PG_BIN/pg_isready.exe" -h 127.0.0.1 -p "$PORT" -U changelens >/dev/null 2>&1; then
  echo "==> Starting PostgreSQL on port $PORT (log: $DATA_DIR/postgres.log)"
  "$PG_BIN/pg_ctl.exe" -D "$DATA_DIR" -l "$DATA_DIR/postgres.log" -o "-p $PORT -h 127.0.0.1" start >/dev/null
  sleep 1
fi

for db in changelens changelens_test; do
  if [[ "$(PSQL "SELECT 1 FROM pg_database WHERE datname='$db'")" != "1" ]]; then
    echo "==> Creating database $db"
    "$PG_BIN/createdb.exe" -h 127.0.0.1 -p "$PORT" -U changelens "$db"
  fi
done

echo "PostgreSQL ready: postgres://changelens@127.0.0.1:$PORT/{changelens,changelens_test} (trust auth, local only)"
