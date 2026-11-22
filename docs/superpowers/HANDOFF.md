# MyGym backend — handoff

**State as of 2026-11-22.** Sub-project 1 (the workout logger) is implemented, reviewed, and
committed. This document records what is verified, what is not, and what to do next.

## Verification state — read this first

| | |
|---|---|
| **Passing** | **248 tests** on a clean build, 0 warnings / 0 errors |
| **Committed but never executed** | **~52 integration tests** across 7 files |
| Commits | 15 tasks + a final review fix wave, `698024c..f8f4ecd` |

The integration tests have **never run**. Docker could not start on the development machine: WSL
was missing, was installed mid-build, and the WSL service needs a reboot that has not happened. See
`.superpowers/sdd/2026-11-21-workout-logger-backend/progress.md` for the full record.

**Nothing about database behaviour is proven.** Not the partial unique index that enforces one
in-progress session per athlete, not the idempotency index behind offline set logging, not
prescription snapshots surviving a round-trip, not the critical path end to end. Those tests exist
and are written to prove exactly those things — they simply have not been given the chance.

EF Core's InMemory provider is deliberately **not** used anywhere, because it ignores the unique
indexes those tests exist to prove. A green suite built on it would be worse than no suite.

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

## Known follow-up work

Deliberately deferred, recorded rather than forgotten. None blocks the merge; several block a public
launch.

### Before a public launch
1. **Token denylist or version claim.** A deleted account's 15-minute access token is still
   cryptographically valid. Write paths that create data now check the user exists, but the durable
   fix is a `deleted_at`/token-version check in `OnTokenValidated`, which also makes the spec's
   revocation claim fully true.
2. **Structured logging.** There is no `ILogger` use anywhere. No log statement, no PII redaction
   policy, no OTel service name, no DB instrumentation. Every 500 currently surfaces as an anonymous
   framework-logged exception with no error code and no correlation id.
3. **Refresh-token hygiene.** No reuse detection, no absolute session lifetime (each rotation grants
   a fresh 30 days), and expired rows are never pruned. A new row is issued on every sign-in.
4. **CORS.** Undecided. Fine if Caddy serves the Mini App and API on one origin; an immediate
   blocker otherwise.
5. **Readiness probe.** `/health` does not check PostgreSQL and nothing consumes it — no Docker
   `HEALTHCHECK`, no compose healthcheck.

### Missing against the spec
6. **`GET /api/exercises?muscle=&equipment=`** — spec §7 specifies both filters; neither exists. The
   `integer[]` muscle storage was chosen for them and is unused.
7. **An application test tier.** Spec §12 and the plan's file structure both name
   `MyGym.Modules.*.Application.Tests`; it was never created. **Three of the four Critical defects
   found in final review lived in that layer** — the only layer with no verified tests. This is the
   single highest-value gap on this list.

### Quality, not correctness
8. **OpenAPI document** — one line each for `AddOpenApi()`/`MapOpenApi()`; the Mini App team has no
   machine-readable contract.
9. **Catalog search projects client-side** — loads up to 100 full aggregates then maps in memory,
   with a hard `Take(100)` and no paging.
10. **`RevisionNumber` jumps by tens per logical edit**, since `PUT` bumps once per day and per
    prescription. Monotonic and traceable, but meaningless as a counter, and the Mini App displays it.
11. **No mixed-unit test** for the top-working-weight query, and **no coverage** of `PATCH`/`DELETE`
    on a logged set.
12. **`global.json` has no SDK pin** despite the plan naming 10.0.303; CI floats on `10.0.x` and the
    Dockerfile on `sdk:10.0`.

Roughly 23 further minor findings are triaged and recorded in the ledger.

## What exists

Three bounded contexts — Identity, ExerciseCatalog, Training — each with Domain, Application,
Infrastructure, and Contracts projects; an API host; and 34 architecture tests that make the module
boundaries a property the build enforces rather than a claim. See the spec for the design and
`README.md` for how to run it.

The spec carries an **Amendments** section recording three places where implementation proved the
document wrong and the document was corrected: the warm-up rounding direction, the placement of the
one-session-per-athlete invariant, and 403-versus-404 for ownership failures.

## Next sub-projects

Unchanged from the spec: progress engine → social layer → AI coach, in that order, each with its own
spec, plan, and implementation cycle. The logger should be used for a few weeks of real training
first — that is what validates the data model while changing it is still cheap.
