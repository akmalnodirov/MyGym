# MyGym

Telegram Mini App for tracking strength training, with a .NET 10 modular-monolith backend.

## Status

Two sub-projects of five are built: the **workout logger** (backend) and the **Mini App** client.
Designs are in `docs/superpowers/specs/`, implementation plans in `docs/superpowers/plans/`, and
the authoritative statement of what is and is not proven is `docs/superpowers/HANDOFF.md`.

| | |
|---|---|
| Backend | 260 tests passing, clean build |
| Mini App | 194 tests passing, typecheck and production build clean |
| Integration tests | **~52 committed, never executed** |
| Real-device run | **never happened** |

Both gaps have the same cause: **Docker cannot start on the development machine** (WSL is installed
but a reboot is pending), so PostgreSQL has never run, and without a running API the client has
only ever been exercised against a mocked one. Nothing about database behaviour is proven —
not the two invariant-enforcing unique indexes, not the idempotency behind set logging, not the
end-to-end critical path.

EF Core's InMemory provider is deliberately not used anywhere, because it ignores the very indexes
those tests exist to prove.

CI runs on a runner with Docker available, so the integration tests, the Docker images and the
Caddyfile are all exercised there. **Treat the first green CI run — or the first local run after
rebooting — as the proof, not this README.**

Planned next: progress engine (volume, estimated 1RM, PR and stall detection), social layer, then
the AI coach.

## Architecture

Three bounded contexts — Identity, ExerciseCatalog, Training — each with clean-architecture layers
inside it. One PostgreSQL database, a schema per module, no cross-schema foreign keys. A module may
reference another module's `Contracts` project and nothing else; `tests/MyGym.ArchitectureTests`
fails the build if that is violated.

The client is a Vite + React 19 + TypeScript single-page app served from the same origin as the
API, which is why CORS is configured nowhere. Its API types are generated from the backend's
OpenAPI document, so a server-side rename is a client compile error. See
`clients/mini-app/README.md`.

## Running locally

Requires the .NET 10 SDK, Node 22, and Docker.

```bash
cp .env.example .env     # then fill in the values
docker compose up --build
```

That brings up PostgreSQL, the API, and Caddy serving the built client. With `MYGYM_DOMAIN` unset
everything is on `https://localhost` behind a local certificate — fine for poking at the stack, and
useless to Telegram, which cannot reach your laptop.

To work on the client without Docker at all:

```bash
cd clients/mini-app
VITE_USE_MSW=1 VITE_TELEGRAM_FAKE=1 npm install && npm run dev
```

That runs against mocked API responses and a fake Telegram runtime, so it needs no database, no API
and no bot.

## Deployment

What stands between a clone of this repository and a working bot.

**1. Host it, with HTTPS.** Telegram requires HTTPS for both a Mini App URL and a webhook.

```bash
MYGYM_DOMAIN=gym.example.com docker compose up -d --build
```

Caddy obtains and renews the certificate itself; the `caddy-data` volume holds it, so do not drop
that volume casually or you will re-request certificates until Let's Encrypt rate-limits you.

The API is not published to the host — Caddy reaches it over the compose network. Everything is one
origin: `/api/*` and `/health` proxy to the API, everything else is the client.

For a first look without a server, a tunnel works: point `cloudflared` or `ngrok` at port 443 and
use the HTTPS URL it gives you.

**2. Create the bot.** [@BotFather](https://t.me/BotFather) → `/newbot` → token. Put it in `.env`
as `TELEGRAM_BOT_TOKEN`, along with `MINIAPP_URL=https://your-domain` and a `JWT_SIGNING_KEY` of at
least 32 bytes.

**3. Register the webhook.** Nothing in the application does this, and until it is done Telegram
sends no updates and the bot never answers:

```bash
BOT_TOKEN=... PUBLIC_URL=https://gym.example.com bash scripts/set-webhook.sh
```

It generates a webhook secret if you have not set one, prints it, and tells you what to configure.
Set `TELEGRAM_WEBHOOK_SECRET` to that value, restart the API, and run it again with
`WEBHOOK_SECRET` set to the same thing. It then reads the registration back and shows
`last_error_message`, which is where "Telegram accepted the URL but cannot deliver" appears.

**4. Seed a program.** There is no authoring UI in v1, by design:

```bash
BOT_TOKEN=... TELEGRAM_USER_ID=<your numeric id> bash scripts/seed-program.sh
```

**5. Send `/start` to the bot.** It replies with a button that opens the Mini App, and the menu
button next to the message box opens it too.

Full options for both scripts are in `scripts/README.md`.

## Tests

```bash
dotnet test                                  # backend
cd clients/mini-app && npm run verify        # client: typecheck + tests
```

Integration tests start a real PostgreSQL 17 container through Testcontainers, so Docker must be
running.

Architecture tests (`tests/MyGym.ArchitectureTests`) need neither a database nor Docker — they are
pure reflection over the already-built assemblies — and were exercised against deliberately
injected violations to confirm each rule can fail, rather than passing by default.

## Configuration

Supply secrets through environment variables, never in `appsettings.json`:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | Database connection string |
| `Telegram__BotToken` | Bot token, used for initData validation and sending messages |
| `Telegram__WebhookSecretToken` | Shared secret for the webhook endpoint; must match what `set-webhook.sh` registered |
| `Jwt__SigningKey` | Access-token signing key, at least 32 bytes |
| `MiniApp__Url` | HTTPS URL the `/start` button opens |
| `MYGYM_DOMAIN` | The hostname Caddy serves and obtains a certificate for |

## Deliberately not built

Carried forward from the specs: auto-progression, 1RM estimates, volume analytics, PR and stall
detection, the transactional outbox, the social layer, the AI coach, and local-first offline sync.

Also absent on purpose in Mini App v1: **program authoring**. Programs are created through the API
with `scripts/seed-program.sh`, because building an editor first would have delayed the only screen
that gets used three times a week.

**No structured logging.** There is no `ILogger` call in the backend, and the client's render errors
reach only the browser console. For now Caddy's access log is the only record that a request
happened. This is the largest known gap; see `docs/superpowers/HANDOFF.md`.
