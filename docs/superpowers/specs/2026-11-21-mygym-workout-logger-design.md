# MyGym — Workout Logger Design (Sub-project 1)

**Date:** 2026-11-21
**Status:** Approved
**Scope:** Sub-project 1 of 4 — the workout logger

## 1. Context

MyGym is a Telegram-based training product: a Mini App for logging workouts, with a bot for
reminders and notifications. The backend is C# on .NET 10. It is intended as a real public
product, not a personal tool, so authentication hardening, rate limiting, and account deletion
are day-one requirements rather than later additions.

The product exists to close the gap that limits most intermediate natural lifters: nobody tracks
whether load and reps actually increased across training blocks, what they ate, or how they slept.
A well-written program on paper is not the constraint; measured progressive overload is.

### Why four sub-projects

The full product idea comprises four largely independent systems:

1. **Workout logger** — programs, exercises, sessions, sets (this document)
2. **Progress engine** — volume, estimated 1RM, PR detection, stall and deload signals
3. **Social layer** — friends, feed, shared results, reactions, leaderboards, moderation
4. **AI coach** — program generation from athlete profile, nutrition guidance, session analysis

They are built in that order because the dependency runs one way: the progress engine needs logged
history, the social layer needs results worth sharing, and the AI coach is worthless without real
training data to reason over. Each sub-project gets its own design, plan, and implementation cycle.
Specifying all four at once would produce a document too vague to implement.

Building the logger first also means it can be used for several weeks of real training before
anything is built on top of it, which validates the data model while changing it is still cheap.

## 2. Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Audience | Real public product | Explicit intent |
| Client | Telegram Mini App, React/TypeScript | Logging 21 sets through chat buttons is unusable; React is the mainstream Mini App path with official SDK support and a small bundle |
| Bot role | Reminders and notifications only | All interactive work happens in the Mini App |
| Workout model | Program-driven, user-authored programs plus curated templates | Supplies prescription-vs-actual data, which is what makes later AI advice possible; also supports athletes who arrive with an existing plan |
| Connectivity | Online-only with optimistic UI | Covers brief gym dropouts at a fraction of the cost of a local-first sync engine, which would roughly double the logger's complexity |
| Architecture | Modular monolith, DDD tactical patterns, clean architecture inside each module | Explicit choice; the Training domain has real invariants that justify a domain layer |
| Database | PostgreSQL, one instance, schema per module | Keeps future module extraction possible without distributed-system cost now |

## 3. Bounded contexts

**Identity & Access** — Telegram account linkage, `AppUser`, athlete profile (age, bodyweight,
units, experience level, available training days, per-session time budget), consent, and deletion.

**Exercise Catalog** — canonical `Exercise` with movement pattern, primary and secondary muscles,
equipment, and localized names plus aliases. Predominantly read-only reference data. Localization
is a functional requirement, not polish: programs are authored in Uzbek, Russian, and English, and
a name such as "Chest Supported Row" must resolve from several spellings and languages.

**Training** — programs and sessions. The core domain.

## 4. Solution structure

Clean architecture layers live inside each module, not across the solution.

```
src/
  Host/MyGym.Api/                     # composition root only: DI, middleware, auth, webhook, module registration
  Modules/
    Identity/        MyGym.Modules.Identity.{Domain,Application,Infrastructure,Contracts}
    ExerciseCatalog/ MyGym.Modules.ExerciseCatalog.{Domain,Application,Infrastructure,Contracts}
    Training/        MyGym.Modules.Training.{Domain,Application,Infrastructure,Contracts}
  Shared/
    MyGym.SharedKernel/                # AggregateRoot, Entity, ValueObject, Result, IDomainEvent, Weight
    MyGym.Shared.Infrastructure/       # EF conventions, auth primitives, telemetry, error mapping
clients/
  mini-app/                            # React + TypeScript Telegram Mini App
tests/
  <per-module>.Domain.Tests
  <per-module>.Application.Tests
  MyGym.IntegrationTests
  MyGym.ArchitectureTests
```

### Boundary rules

- A `Domain` project references only `MyGym.SharedKernel`. No EF Core, no ASP.NET Core, no
  `Microsoft.Extensions.*`.
- A module may reference another module's **`Contracts` project only** — never its `Domain`,
  `Application`, or `Infrastructure`.
- One PostgreSQL database with a schema per module (`identity`, `catalog`, `training`), one
  `DbContext` per module, and **no cross-schema foreign keys**. Cross-module references are stored
  as bare IDs and validated in the application layer.
- Commands go through aggregates via repositories. **Queries bypass the domain entirely** — EF Core
  projections straight to response DTOs. Loading an aggregate to render a history chart is waste,
  and a read-side repository returning entities is the most common way a DDD codebase decays.
- Repositories are aggregate-granular (`ITrainingProgramRepository`, `IWorkoutSessionRepository`),
  declared in `Domain`, implemented in `Infrastructure`. There is no generic `IRepository<T>`.
- These rules are enforced by `NetArchTest` assertions in CI, so a violation fails the build.

## 5. Training domain model

Two aggregates. They never hold object references to each other, only IDs.

```
TrainingProgram            # Id, OwnerUserId, Name, Status, Visibility, ForkedFrom?, RevisionNumber
  └─ ProgramDay            # Order, Label ("Dushanba — Upper Body"), DayOfWeek?
       └─ ExercisePrescription
            # ExerciseId, AlternativeExerciseId?   <- "Leg Press / Walking Lunges"
            # WorkingSetCount, RepRange, RestPeriod, WarmupRamp, Notes

WorkoutSession             # Id, UserId, Status, StartedAt, CompletedAt, BodyweightAtSession?
                           # ProgramId?, ProgramRevision?, ProgramDayId?   <- IDs only
  └─ SessionExercise       # Order, ExerciseId, PrescriptionSnapshot
       └─ LoggedSet        # Order, Kind(Warmup|Working), Weight, Reps, Rir?, ClientSetId
```

### Value objects

`Weight(decimal Value, WeightUnit Unit)`, `RepRange(int Min, int Max)`,
`RestPeriod(TimeSpan Min, TimeSpan Max)`, `WarmupRamp` (an ordered list of
`WarmupStep(decimal PercentOfWorking, int Reps, bool IsOptional)`), `WeightIncrement`, and
strongly-typed identifiers `UserId`, `ProgramId`, `SessionId`, `ExerciseId`.

### Prescription snapshots

When a session starts, it copies the prescribed exercises into `PrescriptionSnapshot` as owned
value objects. The program is referenced only by `ProgramId` plus `ProgramRevision`, for
traceability.

This is the single most important modelling decision in the logger. Without it, editing a program
in January silently rewrites what November's sessions "prescribed", and every adherence figure and
stall signal that sub-project 2 derives from that history becomes false. Programs carry a monotonic
`RevisionNumber` bumped on each edit; there is no full version history, which the product does not
need.

### Status lifecycles

- `TrainingProgram.Status`: `Draft` → `Active` → `Archived`. `Visibility`: `Private` | `Public`.
- `WorkoutSession.Status`: `InProgress` → `Completed` | `Abandoned`. Terminal states are final.

### Invariants enforced in the domain

Enforced by the aggregates themselves, not by API validation or database constraints alone:

1. `WarmupRamp` percentages are strictly ascending and all below 100%.
2. `WorkingSetCount` is between 1 and 10 inclusive.
3. `RepRange.Min <= RepRange.Max`, and `Min >= 1`.
4. `ProgramDay.Order` values are contiguous from 1 within a program; so are
   `ExercisePrescription.Order` within a day, `SessionExercise.Order` within a session, and
   `LoggedSet.Order` within a session exercise.
5. A program must have at least one day, and each day at least one prescription, to be activated.
6. No set may be logged, edited, or deleted on a `Completed` or `Abandoned` session. Closed history
   is immutable.
7. Completing a session requires at least one logged working set.
8. `LoggedSet.Reps` is between 0 and 100; `Weight.Value` is non-negative and at most 1000 kg
   equivalent.

### At most one `InProgress` session per user

Unlike the invariants above, this one is **not** enforced by an aggregate, because it cannot be: a
`WorkoutSession` aggregate has no way to see its siblings, so no in-aggregate check can guarantee
there is only one `InProgress` row for a given user across the whole table. It is instead an
application-level pre-check (`GetInProgressForUserAsync` before `Start`) backed by the real
guarantee, a partial unique index on `(UserId)` where `Status = InProgress` — the same
application-check-plus-database-index shape idempotent set logging below uses for the same reason.
This is also the guard that makes optimistic client writes safe: without it, a retry storm on poor
signal creates duplicate sessions. (See the Amendments note at the end of this document.)

### Idempotent set logging

The client generates a `ClientSetId` GUID for each set. `LogSet` is idempotent on
`(SessionExerciseId, ClientSetId)`, enforced by a unique index. The Mini App's retry badge may
therefore fire as many times as the connection demands without producing phantom sets. This
directly serves the online-only optimistic-UI decision.

### Warm-up resolution

A pure domain function, not a service with dependencies:

```csharp
WarmupRamp.Resolve(Weight working, WeightIncrement rounding) -> IReadOnlyList<ResolvedWarmupStep>
```

Given 80 kg and a bench ramp of 27% x 10, 53% x 6, 80% x 3, it returns 20 x 10, 40 x 6, 62.5 x 3,
rounded *down* to increments that physically exist — never rounded to nearest, which could ask the
athlete to load more than the prescribed percentage. No database, no mocks, exhaustively
unit-testable. (See the Amendments note at the end of this document.)

### Ad-hoc sessions

An ad-hoc session is the same `WorkoutSession` aggregate with `ProgramId` null and no prescription
snapshot; the athlete chooses exercises freely. There is no second model.

### Domain events

Declared in `MyGym.Modules.Training.Contracts`: `WorkoutSessionStarted`, `WorkoutSessionCompleted`,
`ProgramActivated`.

For sub-project 1 these are dispatched in-process after `SaveChanges`, within the same transaction.
**No outbox is built yet.** The outbox arrives in sub-project 2, when the progress engine becomes
the first real subscriber; building it now would be infrastructure serving a consumer that does not
exist.

## 6. Authentication

The Mini App sends Telegram `initData`. The server validates its HMAC-SHA256 signature using a
secret key derived as `HMAC_SHA256("WebAppData", botToken)` over the sorted data-check-string,
compared in constant time, and rejects `initData` whose `auth_date` is older than one hour.

Valid `initData` is exchanged once for the application's own tokens: a 15-minute JWT access token
and a longer-lived refresh token. Re-validating the HMAC on every request is avoided; owning the
tokens gives us our own claims, revocation on account deletion, and a stable user identity the bot
can address notifications to. Implemented as a custom `AuthenticationHandler` in
`MyGym.Shared.Infrastructure`.

The bot webhook endpoint is separately guarded by verifying the
`X-Telegram-Bot-Api-Secret-Token` header against the configured secret.

## 7. API surface

```
POST   /api/auth/telegram                           initData -> access + refresh tokens
POST   /api/auth/refresh

GET    /api/me
PATCH  /api/me                                      age, bodyweight, units, experience, available days
DELETE /api/me                                      hard delete of account and all training data

GET    /api/exercises?q=&muscle=&equipment=         localized catalog search

GET    /api/programs                                owned programs + public templates
POST   /api/programs                                create draft
GET    /api/programs/{id}
PUT    /api/programs/{id}                           edit, bumps RevisionNumber
POST   /api/programs/{id}/activate
POST   /api/programs/{id}/fork
DELETE /api/programs/{id}                           archive
GET    /api/programs/{id}/days/{dayId}/preview      resolved warm-ups + suggested working weights

POST   /api/sessions                                start from program day, or ad-hoc
GET    /api/sessions/current
GET    /api/sessions?from=&to=&page=                history
GET    /api/sessions/{id}
POST   /api/sessions/{id}/exercises/{seId}/sets     idempotent on clientSetId
PATCH  /api/sessions/{id}/exercises/{seId}/sets/{setId}
DELETE /api/sessions/{id}/exercises/{seId}/sets/{setId}
POST   /api/sessions/{id}/complete
POST   /api/sessions/{id}/abandon

POST   /api/telegram/webhook                        bot updates, secret-token guarded
```

## 8. Critical path

Starting a session and logging a set is the flow that must be fast and reliable:

1. Client posts `{ programId, dayId }` to `/api/sessions`.
2. `StartSessionCommand` loads the program, asserts the athlete has no `InProgress` session,
   builds the prescription snapshot, and resolves each exercise's warm-up ramp against the last
   session's top working set for that exercise.
3. The aggregate is persisted and `WorkoutSessionStarted` is raised.
4. The response carries the complete session with concrete suggested weights already filled in, so
   the athlete sees "Incline DB Press — warm-up 12.5x10, 17.5x5, 22.5x3, then 3x6-8 @ 27.5" without
   typing anything.
5. Each set is one idempotent POST carrying its `clientSetId`.
6. `POST /complete` transitions the aggregate and raises `WorkoutSessionCompleted`.

### Deliberate limitation

Sub-project 1 suggests the **same** working weight as the previous session. There is no
auto-progression. Judging whether an athlete earned an increase requires trend analysis across
sessions, which is exactly what the progress engine provides; approximating it here would mean
building a worse version of logic that is next in the queue.

## 9. Error handling

Two mechanisms for two different kinds of failure:

- **Expected business failures** — a second concurrent session, editing closed history, a reversed
  rep range — are returned as `Result<T>` from application handlers. These are normal outcomes.
- **Unreachable states** — an aggregate loaded in a structurally invalid shape — throw
  `DomainException`. These are bugs and must surface loudly in telemetry.

Both map to RFC 9457 `ProblemDetails` through one centralized mapper. Every error carries a stable
machine-readable code (`session.already_in_progress`, `warmup.percent_not_ascending`) rather than an
English sentence, so the Mini App localizes from the code and the backend never holds UI copy in
any language.

HTTP mapping: 409 for state conflicts, 422 for validation failures, 404 for missing **or
non-owned** resources, 429 for rate limits, 401 for authentication. A resource that exists but is
not owned by the caller returns 404, not 403: a 403 confirms the id is real and merely forbidden,
which lets an attacker enumerate every other athlete's program and session ids by watching for the
403/404 split. 404 for both cases is indistinguishable from the outside. (See the Amendments note
at the end of this document.)

## 10. Non-functional requirements

- Per-user rate limiting via ASP.NET Core's built-in `RateLimiter`, partitioned on user ID.
- Request body size caps on all write endpoints.
- `DELETE /api/me` performs a real hard delete across all module schemas and revokes refresh
  tokens.
- OpenTelemetry traces and metrics; structured logging that carries no personal data beyond the
  Telegram user ID.
- All timestamps stored as UTC; the athlete's time zone is held on the profile for display and
  reminder scheduling.

## 11. Infrastructure

PostgreSQL with per-module EF Core migrations applied at startup behind a migration gate. A single
Docker image for the API, and `docker-compose` for local development with Postgres. Deployment to a
small VPS behind Caddy for automatic TLS — Telegram webhooks require public HTTPS, and this is the
cheapest route to a valid certificate. This component is replaceable with a managed container host
without touching application code.

## 12. Testing strategy

Tests are written before implementation.

- **Domain unit tests** — pure, zero mocks. Table-driven coverage of `WarmupRamp.Resolve` rounding
  behaviour, the session state machine, and every invariant in section 5. This is the bulk of the
  suite and runs in milliseconds.
- **Application tests** — command and query handlers against the same Testcontainers PostgreSQL
  fixture used by integration tests, with real repository implementations. Repositories are never
  mocked; only genuinely external services are faked.
- **Integration tests** — `WebApplicationFactory` with **Testcontainers PostgreSQL**, explicitly
  not EF Core's InMemory provider, which ignores unique indexes and constraints and would therefore
  pass the very idempotency and one-active-session tests that matter most while the real database
  rejected them. The critical path is covered end to end, including posting the same `clientSetId`
  twice and asserting exactly one set exists.
- **Architecture tests** — `NetArchTest` asserting the boundary rules in section 4.

The exercise catalog ships seeded with the exercises from the author's own program, localized,
which makes real use possible on day one.

## 13. Out of scope

Deferred to later sub-projects, and deliberately absent here:

- Auto-progression, estimated 1RM, volume analytics, PR detection, stall and deload signals (#2)
- Transactional outbox (#2, when the first subscriber exists)
- Friends, feed, reactions, leaderboards, sharing, moderation (#3)
- AI program generation, nutrition guidance, session analysis (#4)
- Local-first offline sync, media uploads, wearable integrations, payments (not planned)

## Amendments

Three corrections made after implementation and a final whole-branch review revealed the document,
not the code, was wrong:

1. **§5's warm-up example** originally gave 80 kg at 27/53/80% as 22.5 x 10, 42.5 x 6, 65 x 3
   (round-to-nearest). The implementation rounds *down* and returns 20 x 10, 40 x 6, 62.5 x 3,
   which is correct: §5's own prose says a warm-up "rounded to increments that physically exist"
   must never ask the athlete to load more than the prescribed percentage, and rounding to nearest
   can round up. Corrected to match the implementation.
2. **§5's invariant list** originally included "at most one `InProgress` session per user" among
   invariants "enforced in the domain... not by API validation or database constraints alone." A
   `WorkoutSession` aggregate cannot see its sibling rows, so no in-aggregate check can enforce a
   constraint across the whole table — this was always structurally unenforceable in the domain as
   originally described. Moved out of that list into its own section describing the
   application-check-plus-partial-unique-index shape the implementation actually uses, which is the
   only correct way to enforce it.
3. **§9's HTTP mapping** originally specified 403 for ownership failures. The implementation
   deliberately returns 404 for a resource that exists but is not owned by the caller, specifically
   to prevent id enumeration — a 403 confirms the id is real, which a 404 does not. Amended to
   specify 404, with the anti-enumeration reasoning recorded alongside it.
