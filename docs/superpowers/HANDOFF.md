# MyGym — handoff

**State as of 2026-11-23.** Sub-project 1 (the backend workout logger) and sub-project 2 (the
Telegram Mini App client) are both implemented, reviewed and committed. This document records what
is verified, what is not, and what to do next.

## Verification state — read this first

| | |
|---|---|
| **Backend, passing** | **260 tests**, clean build, 0 warnings |
| **Mini App, passing** | **194 tests**, typecheck clean, production build clean |
| **Committed but never executed** | **~52 backend integration tests** across 7 files |
| **Never run on a real device** | the entire Mini App |

Two things have never been exercised, and both are blocked on the same cause.

**PostgreSQL has never run here.** Docker could not start on the development machine: WSL was
missing, was installed mid-build, and the WSL service needs a reboot that has not happened. So
nothing about database behaviour is proven — not the partial unique index enforcing one in-progress
session per athlete, not the idempotency index behind offline set logging, not prescription
snapshots surviving a round-trip, not the critical path end to end. Those tests exist and are
written to prove exactly those things; they have simply never been given the chance.

EF Core's InMemory provider is deliberately **not** used anywhere, because it ignores the unique
indexes those tests exist to prove. A green suite built on it would be worse than no suite.

**The Mini App has never run inside Telegram.** It has been exercised against a fake Telegram
runtime and a mocked API, which covers the chrome wiring and every data path, but not the real
webview, the real `initData`, or a real network. See `clients/mini-app/README.md` for how to do
that pass.

## First thing after the reboot

```bash
# 1. Confirm the engine is genuinely up — not just that `docker info` exited 0.
docker version --format '{{.Server.Version}}'   # must print a version
docker ps                                        # must return, not hang

# 2. Run the suite that has never run.
dotnet test tests/MyGym.IntegrationTests
```

Expect failures on first contact. ~52 tests are meeting a real PostgreSQL for the first time, and
several exercise behaviour that has only ever been reasoned about. That is the point of running
them.

### Tests most worth watching

- `TrainingPersistenceTests` — the two database-enforced invariants. Both assert on the **index
  name** in the Postgres error, so a wrong or missing index fails loudly rather than silently.
- `SessionEndpointTests.The_critical_path_runs_end_to_end` — the whole product in one test: start a
  program day, log warm-ups and working sets, complete, then confirm the next preview suggests the
  weight actually lifted with warm-ups resolved to `[20, 40, 62.5]`.
- `SessionEndpointTests.Changing_the_preferred_unit_does_not_break_the_session_or_the_preview` —
  reproduces a Critical defect found in final review.
- `AccountDeletionTests` — hard deletion across two schemas, verified by querying both contexts
  directly rather than trusting the API.
- The two Npgsql exception-message assertions (unique telegram id, unique slug) are the most likely
  to need adjusting, since they depend on message formatting.

### Then: the Mini App on a real device

1. Start PostgreSQL and the API.
2. Seed the 3-day program:

   ```bash
   BOT_TOKEN=<the same token the API is configured with> TELEGRAM_USER_ID=<your numeric id> \
     bash scripts/seed-program.sh
   ```

   `TELEGRAM_USER_ID` is your numeric Telegram id — ask @userinfobot. See `scripts/README.md` for
   every option.

   There is no authoring UI in v1 — that is deliberate (Mini App design spec §1). The script talks
   to the public API, signs a real `initData` with the bot token, resolves catalog slugs to ids,
   and activates the program. Re-running it replaces the program rather than duplicating it, so
   editing `scripts/programs/akmal-3day.json` and running again is the edit loop.

   The file's contents are validated without a server by
   `tests/MyGym.Modules.Training.Domain.Tests/SeedProgramFileTests.cs`, so a bad percentage fails
   as a unit test rather than as an HTTP 422 against a live database.

3. Tunnel the dev server, point a real bot's Mini App URL at the tunnel, and open it in Telegram.
4. Walk the critical path: sign in, pick today, log a set, finish.

The things most likely to behave differently from the fake: `initData` signature validation against
a real bot token, the Main and Back buttons' real appearance and ordering, viewport height with the
keyboard open, and the closing-confirmation prompt on a swipe-down.

## Known follow-up work

Deliberately deferred, recorded rather than forgotten.

### Before a public launch
1. **Token denylist or version claim.** A deleted account's 15-minute access token is still
   cryptographically valid. Write paths that create data now check the user exists, but the durable
   fix is a `deleted_at`/token-version check in `OnTokenValidated`.
2. **Structured logging.** There is no `ILogger` use anywhere in the backend. No log statement, no
   PII redaction policy, no OTel service name, no DB instrumentation. Every 500 surfaces as an
   anonymous framework-logged exception with no error code and no correlation id. The Mini App has
   the matching gap: render errors reach only the browser console, because there is no
   client-error endpoint to send them to.
3. **Refresh-token hygiene.** No reuse detection, no absolute session lifetime (each rotation
   grants a fresh 30 days), and expired rows are never pruned. **The Mini App makes this grow
   faster than originally expected**: it holds no tokens across launches by design, so every app
   open exchanges `initData` and issues a new `refresh_tokens` row.
4. **CORS.** Resolved as "not needed", provided Caddy serves the Mini App and the API on one
   origin, which is what `VITE_API_BASE_URL` defaulting to `/api` assumes. An immediate blocker if
   that changes.
5. **Readiness probe.** `/health` does not check PostgreSQL and nothing consumes it — no Docker
   `HEALTHCHECK`, no compose healthcheck.
6. **No Caddyfile exists yet.** The single-origin deployment the design depends on is described but
   not configured anywhere in this repository.

### Missing against the specs
7. **`GET /api/exercises?muscle=&equipment=`** — backend spec §7 specifies both filters; neither
   exists. The `integer[]` muscle storage was chosen for them and is unused. v1 of the Mini App
   only needs id→name, so this does not block, but an authoring UI will need it.
8. **An application test tier.** Backend spec §12 and its plan both name
   `MyGym.Modules.*.Application.Tests`; it was never created. **Three of the four Critical defects
   found in the backend's final review lived in that layer** — the only layer with no verified
   tests. This remains the single highest-value gap on this list.
9. **Program authoring UI.** Deliberately out of scope for Mini App v1. The program is seeded
   through the API. This is the next obvious piece of client work.

### Quality, not correctness
10. **Catalog search projects client-side** — loads up to 100 full aggregates then maps in memory,
    with a hard `Take(100)` and no paging.
11. **`RevisionNumber` jumps by tens per logical edit**, since `PUT` bumps once per day and per
    prescription. Monotonic and traceable, but meaningless as a counter. The Mini App displays it
    nowhere, for that reason.
12. **No mixed-unit test** for the top-working-weight query, and **no coverage** of `PATCH`/`DELETE`
    on a logged set from the API side. The Mini App does not call either endpoint in v1.
13. **`global.json` has no SDK pin** despite the plan naming 10.0.303; CI floats on `10.0.x` and
    the Dockerfile on `sdk:10.0`.
14. **Mini App writes do not survive a cold start.** A failed set retries while the app is open but
    is not persisted to disk. The `clientSetId` idempotency key makes durable queueing a later
    addition rather than a rewrite.
15. **No end-to-end browser tier for the Mini App.** It would test the fake Telegram runtime rather
    than Telegram; the real verification is the device pass above.

Roughly 23 further minor backend findings are triaged and recorded in the ledger at
`.superpowers/sdd/2026-11-21-workout-logger-backend/progress.md`.

## What exists

**Backend.** Three bounded contexts — Identity, ExerciseCatalog, Training — each with Domain,
Application, Infrastructure and Contracts projects; an API host; and 34 architecture tests that make
the module boundaries a property the build enforces rather than a claim. 22 endpoints across
`/api/auth`, `/api/me`, `/api/programs`, `/api/exercises` and `/api/sessions`, plus a Telegram
webhook.

The backend spec carries an **Amendments** section recording three places where implementation
proved the document wrong: the warm-up rounding direction, the placement of the
one-session-per-athlete invariant, and 403-versus-404 for ownership failures.

**Contract.** The API publishes an OpenAPI document in Development at `/openapi/v1.json`;
`scripts/dump-openapi.sh` writes it with no database required. `clients/mini-app/openapi.json` and
`clients/mini-app/src/api/generated.ts` are committed copies, and a CI job fails if either goes
stale.

**Mini App.** Vite + React 19 + TypeScript, TanStack Query for all server state, five screens, and
no hand-written API types. See `clients/mini-app/README.md` for the architecture and the three
things worth knowing before changing it.

## Next sub-projects

Unchanged: progress engine → social layer → AI coach, in that order, each with its own spec, plan
and implementation cycle. The logger should be used for a few weeks of real training first — that
is what validates the data model while changing it is still cheap.
