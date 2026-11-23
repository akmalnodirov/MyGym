#!/usr/bin/env bash
# Thin wrapper over set_webhook.py, matching seed-program.sh: one entry point, and the interpreter
# name does not have to be guessed (Git Bash on Windows has `python`, Linux and CI have `python3`).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if command -v python3 >/dev/null 2>&1; then
  PY=python3
elif command -v python >/dev/null 2>&1; then
  PY=python
else
  echo "error: python 3 is required but was not found on PATH" >&2
  exit 1
fi

exec "$PY" "$ROOT/scripts/set_webhook.py" "$@"
