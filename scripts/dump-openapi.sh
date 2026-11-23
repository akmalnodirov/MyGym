#!/usr/bin/env bash
# Writes the API's OpenAPI document to the path given as $1 (default: clients/mini-app/openapi.json).
#
# The document is served only in Development, so this starts the host in Development with
# migrations disabled and placeholder secrets. Nothing here touches PostgreSQL, which is why this
# works on a machine with no database running -- the document is derived from endpoint metadata,
# not from data.
set -euo pipefail

OUT="${1:-clients/mini-app/openapi.json}"
PORT="${OPENAPI_PORT:-5199}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:${PORT}"
export RunMigrationsOnStartup=false
# Placeholders: eager options validation refuses to boot without them. They never sign a token
# here -- the process exits as soon as the document is written.
export Jwt__Issuer=openapi-dump
export Jwt__Audience=openapi-dump
export Jwt__SigningKey=openapi-dump-signing-key-not-a-real-secret
export Telegram__BotToken=0:openapi-dump
export Telegram__WebhookSecretToken=openapi-dump
export MiniApp__Url=https://example.invalid

dotnet run --project src/Host/MyGym.Api --no-launch-profile >/tmp/openapi-host.log 2>&1 &
HOST_PID=$!
trap 'kill "$HOST_PID" 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${PORT}/openapi/v1.json" -o "$OUT.tmp" 2>/dev/null; then
    mkdir -p "$(dirname "$OUT")"
    mv "$OUT.tmp" "$OUT"
    echo "wrote $OUT ($(wc -c <"$OUT") bytes)"
    exit 0
  fi
  kill -0 "$HOST_PID" 2>/dev/null || { echo "host exited; log:"; cat /tmp/openapi-host.log; exit 1; }
  sleep 1
done

echo "timed out waiting for /openapi/v1.json; log:" >&2
cat /tmp/openapi-host.log >&2
exit 1
