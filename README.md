# MyGym

Telegram Mini App for tracking strength training, with a .NET 10 modular-monolith backend.

## Status

Sub-project 1 of 4 — the workout logger. See `docs/superpowers/specs/` for the design and
`docs/superpowers/plans/` for the implementation plan.

Planned next: progress engine (volume, estimated 1RM, PR and stall detection), social layer,
then the AI coach.

**Integration tests are committed but have not been run locally.** They start a real PostgreSQL
17 container via Testcontainers, which needs a working Docker daemon; on this machine Docker
cannot currently start (WSL is installed but a reboot is pending). Everything that does not need
Docker — all unit tests and the architecture tests below — has been built and run, and is green.
The database-backed behaviour (persistence, the two invariant-enforcing unique indexes, the
end-to-end critical path against a real instance) is correct by code review and by the design in
`docs/superpowers/specs/`, but is not yet proven by an executed test run on this machine. The
GitHub Actions workflow in `.github/workflows/ci.yml` runs on a runner with Docker available, so
the integration tests execute there — treat the first green CI run (or the first local run after
rebooting) as the actual proof, not this README.

## Architecture

Three bounded contexts — Identity, ExerciseCatalog, Training — each with clean-architecture layers
inside it. One PostgreSQL database, a schema per module, no cross-schema foreign keys. A module may
reference another module's `Contracts` project and nothing else; `tests/MyGym.ArchitectureTests`
fails the build if that is violated.

## Running locally

Requires .NET 10 SDK and Docker.

    cp .env.example .env     # then fill in the values
    docker compose up --build

The API listens on http://localhost:8080; `GET /health` should return `{"status":"ok"}`.

(This sequence has not been executed on this machine — see Status above. The Dockerfile and
compose file have been read carefully against this repo's build layout and `docker compose config`
parses cleanly, but `docker build`/`up` themselves are unverified pending the Docker/WSL reboot.)

## Tests

    dotnet test

Integration tests start a real PostgreSQL 17 container through Testcontainers, so Docker must be
running. EF Core's InMemory provider is deliberately not used: it ignores the unique indexes that
enforce one-session-per-athlete and idempotent set logging.

Architecture tests (`tests/MyGym.ArchitectureTests`) need neither a database nor Docker — they are
pure reflection over the already-built assemblies — and are the one test suite in this project that
has actually been exercised against the real code, including against deliberately injected
violations, to confirm each rule can fail and not just pass by default.

## Configuration

Supply secrets through environment variables, never in `appsettings.json`:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | Database connection string |
| `Telegram__BotToken` | Bot token, used for initData validation and sending messages |
| `Telegram__WebhookSecretToken` | Shared secret for the webhook endpoint |
| `Jwt__SigningKey` | Access-token signing key, at least 32 bytes |
| `MiniApp__Url` | HTTPS URL the `/start` button opens |

## Deliberately not built

Carried forward from spec §13: auto-progression, 1RM estimates, volume analytics, PR and stall
detection, the transactional outbox, the social layer, the AI coach, and local-first offline sync.
The React Mini App client is the subject of its own plan.
