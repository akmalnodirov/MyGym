# MyGym Mini App — design

**Sub-project 2 of 4.** The Telegram Mini App client for the workout logger built in
sub-project 1. Written 2026-11-23.

**Depends on:** `docs/superpowers/specs/2026-11-21-mygym-workout-logger-design.md` (the backend
spec, which this document treats as the authority on the API) and commit `17d7e33`, which added the
OpenAPI document this client generates its types from.

---

## 1. Purpose and scope

The athlete opens Telegram, taps into MyGym, sees today's workout already filled in with the weights
they are expected to lift, and taps once per set to confirm what they actually lifted. That is the
whole of v1.

The product this replaces is a text file: three days of prescriptions with percentage-based warm-up
ramps and rest times, read off a phone between sets. The Mini App has to be faster than reading that
file, or the athlete will keep reading the file.

### In scope

1. Sign in from the Telegram runtime, with no visible login step.
2. See the active program and today's day.
3. Preview a day before starting it.
4. Log a workout: warm-ups and working sets, pre-filled, one tap each.
5. A rest timer that starts itself when a set is logged.
6. Finish or abandon a session.
7. History: past sessions, and one session read-only.
8. Profile: preferred unit, loading increment, bodyweight.

### Out of scope for v1

- **Program authoring.** The athlete's own 3-day program is seeded once through the API with a
  script. A UI for building and editing programs is its own piece of work, and building it first
  would delay the only screen that gets used three times a week. `GET /api/programs` and
  `PUT /api/programs/{id}` exist and are not called by this client except to read.
- Social features, progress charts, AI coaching. Those are sub-projects 3 and 4.
- Offline queueing across app launches. See §7 — writes survive a dropped connection within a
  session, but not a cold start.
- Internationalisation beyond what the exercise catalog already returns per `lang`. The UI copy is
  English in v1.

### Success criteria

- Logging a set takes one tap and the next screen state is visible without waiting for the network.
- A dropped connection mid-workout never produces a duplicated or lost set.
- The session screen renders from a single API response.

---

## 2. Stack and project structure

**Vite + React 19 + TypeScript**, built to static files. No server-side rendering: the app runs
inside a Telegram webview, there is no SEO, authentication is client-side `initData`, and every byte
of data is private to one athlete. A Node server would add a deployment unit that buys nothing.

**TanStack Query owns all server state.** This is the load-bearing choice. The backend was built for
optimistic writes with idempotent retries — a client-generated `ClientSetId` behind a unique index,
so replaying a set-log request returns the original set instead of creating a second one. Query's
optimistic-mutation and retry machinery maps onto that one-to-one. The alternative is hand-writing
the queue, the backoff, and the cache reconciliation, which is the part of this app that must not be
subtly wrong: it is what protects a logged set when the signal drops in a basement gym.

**Client state is deliberately small**: the rest timer and the pending-set-id map (§7). Both live in
React context with a reducer. No Redux, no Zustand — there is not enough client state to justify a
library, and server state is not client state.

**Routing**: `react-router` in `createBrowserRouter` form, five routes. A router rather than a
`useState` screen switch because Telegram's Back button must map to history depth (§5).

### Directory layout

```
clients/mini-app/
  package.json
  vite.config.ts
  tsconfig.json
  openapi.json                 generated, committed  (see §3)
  index.html
  src/
    main.tsx                   mounts <App/>, installs the Telegram runtime
    App.tsx                    providers + router
    routes.tsx                 route table, one place
    api/
      generated.ts             generated from openapi.json, never hand-edited
      client.ts               fetch wrapper: base URL, bearer token, ProblemDetails -> ApiError
      queries.ts              query keys + useQuery hooks
      mutations.ts            useMutation hooks, incl. the optimistic set log
    telegram/
      runtime.ts              the TelegramRuntime interface + real implementation
      mock-runtime.ts         dev-only fake (see §9)
      useBackButton.ts
      useMainButton.ts
      useThemeParams.ts
    auth/
      AuthProvider.tsx        initData -> tokens, in-memory, refresh on 401
    session/
      SessionScreen.tsx
      ExerciseCard.tsx
      SetRow.tsx
      useSetRows.ts           derives the row model (§6)
      usePendingSetIds.ts     the idempotency-key map (§7)
      RestTimer.tsx
      RestTimerProvider.tsx
    today/TodayScreen.tsx
    preview/DayPreviewScreen.tsx
    history/HistoryScreen.tsx
    profile/ProfileScreen.tsx
    catalog/useExerciseNames.ts
    ui/                        Button, Stepper, Sheet, Spinner, ErrorBanner
    errors/codes.ts            the error-code -> message/behaviour table (§10)
  test/
    msw/handlers.ts
    msw/fixtures.ts
    setup.ts
```

Feature-first, not layer-first: the files that change together live together. `SessionScreen`,
`ExerciseCard`, `SetRow`, and the two hooks that feed them are one unit of work and one directory.

---

## 3. The contract: generated types

`clients/mini-app/openapi.json` is generated from the running API by `scripts/dump-openapi.sh` and
**committed**. `src/api/generated.ts` is generated from it by `openapi-typescript` via
`npm run api:types`, and is also committed.

Both are committed on purpose. A developer — or a CI job, or a fresh clone — can typecheck the
client without a .NET SDK, a PostgreSQL instance, or a running API. The cost is that the pair can go
stale; the control for that is a CI step that regenerates both and fails if `git diff` is non-empty,
which turns staleness into a red build rather than a runtime surprise.

**Nothing in `src/` hand-declares an API shape.** A server-side rename of
`SessionExerciseResponse.SuggestedWorkingWeight` becomes a TypeScript compile error here, which is
the entire reason the document exists.

Two facts about the document that the client design depends on:

- `PreviewExerciseResponse` and `SessionExerciseResponse` identify exercises by `ExerciseId` only.
  **There is no exercise name in either.** The client must hold the catalog and join. See §8.
- `SessionExerciseResponse.suggestedWorkingWeight` is a bare `number | null`. **The unit is not on
  it**, so it is the athlete's `preferredUnit` from `GET /api/me` and the profile must be loaded
  before any weight on a session screen renders. Two neighbouring types differ here and the
  difference is easy to get wrong: `PreviewExerciseResponse` *does* carry `weightUnit`, so the day
  preview needs no profile; and `LoggedSetResponse` carries its own `unit`, because a set logged
  last month may have been logged in the other unit.
- **Property names are camelCase and enums are string unions.** `dayOfWeek` is
  `"Sunday" | … | "Saturday" | null`, `unit` is `"Kilograms" | "Pounds"`, `kind` is
  `"Warmup" | "Working"`. The weekday mapping to JavaScript's `Date.getDay()` must therefore be an
  explicit table, not a cast.
- **Numbers are numbers.** .NET initially described every `int` and `decimal` as
  `number | string`, because its deserialiser accepts quoted numbers on input. A schema transformer
  (`366fcce`) narrows those to the numeric type while preserving `null`, so the client does no
  coercion and `rir`/`suggestedWorkingWeight` still force the absent case to be handled.

---

## 4. Authentication

No persisted tokens, no login screen.

On launch, the app reads `initData` from the Telegram runtime and `POST`s it to
`/api/auth/telegram`, receiving an access token (15 minutes) and a refresh token (30 days). The
access token is held **in a module-scoped variable, not `localStorage`**. On a 401 the client
refreshes once and replays the request; if the refresh also fails
(`auth.refresh_token_invalid`), it re-exchanges `initData`, which always succeeds inside Telegram
because `initData` is present on every launch.

The refresh token is therefore never persisted either. A cold start always re-exchanges `initData`.
This removes token storage, cross-launch expiry handling, and an XSS exfiltration target in one
stroke, and costs one extra request per launch.

**Consequence for the backend**: every launch issues a new `refresh_tokens` row and nothing prunes
them. That is item 3 on the backend's follow-up list and this design makes it grow faster than
expected — one row per app open rather than one per sign-in. Not a v1 blocker; recorded so it is not
a surprise.

**There is no development auth bypass.** A `?dev=1` that mints a token without a signature check is
a production authentication bypass one misconfiguration away from being live, and it would mean the
real auth path is never exercised in development. Development instead fakes the *Telegram runtime*
(§9) and lets the real signature-checked path run against a real local API, or against a mocked one.

---

## 5. Screens and Telegram chrome

Five routes:

| Route | Screen | Primary data |
|---|---|---|
| `/` | Today | `GET /api/programs`, `GET /api/sessions/current` |
| `/programs/:programId/days/:dayId` | Day preview | `GET /api/programs/{id}/days/{dayId}/preview` |
| `/sessions/:sessionId` | Session (live or read-only) | `GET /api/sessions/{id}` |
| `/history` | History | `GET /api/sessions?page=&pageSize=` |
| `/profile` | Profile | `GET /api/me` |

**Today** lists the active program's days with the day whose `DayOfWeek` matches the device weekday
pulled to the top and labelled "Today". `DayOfWeek` is nullable, so a program with unscheduled days
simply shows them in `Order` with nothing promoted. If `GET /api/sessions/current` returns **200**
rather than 204, this screen leads with *Resume workout* and the day list drops below it — a
half-finished workout is the only reason the app was opened.

**Day preview** is the screen that replaces the text file: exercises, resolved warm-up weights,
target reps, rest. It gets its own route rather than being a sheet, because it is read on its own
between sessions. Its one action starts the session.

**Session** is §6.

**History** pages `GET /api/sessions`. Tapping a row opens `/sessions/:id`, which renders the same
`SessionScreen` in read-only mode when `Status !== "InProgress"`. One component with a mode flag,
not two components that drift apart.

**Profile** is `GET`/`PATCH /api/me`. Small, but `PreferredUnit` and `LoadingIncrement` govern every
weight and every stepper in the app.

### Telegram's native chrome

The Back button and the Main button are **single global objects**, not per-screen widgets. Binding a
handler in a component and failing to unbind it on unmount leaves a stale closure firing a stale
action — the canonical bug of this platform. So:

- `useBackButton()` — shows the button when history depth > 0, hides it at the root, and routes its
  click to `navigate(-1)`. Subscribes on mount, unsubscribes on unmount.
- `useMainButton({ text, onClick, visible, loading })` — one hook owns the button. Screens declare
  intent; nothing else touches `WebApp.MainButton`. Carries *Start workout* on the preview and
  *Finish workout* on a live session.

Two more runtime calls that are easy to omit and visible when missing:

- `WebApp.expand()` at launch, or the app opens as a half-height sheet.
- `WebApp.enableClosingConfirmation()` **while and only while a session is in progress**. Without
  it, a swipe-down mid-workout closes the app silently. The session survives server-side, but it
  reads as data loss. Enabled when a live session mounts, disabled when it completes or unmounts.

`useThemeParams()` maps Telegram's `themeParams` onto CSS custom properties on `:root`, so the app
matches the user's Telegram theme rather than shipping its own light/dark decision.

---

## 6. The session screen

One `GET /api/sessions/{id}` renders the whole screen. The backend's `SessionMapper` was written for
this: it enriches each exercise with resolved warm-up weights and the suggested working weight, so
there is no second request and no client-side percentage arithmetic.

### Layout

```
┌──────────────────────────────────┐
│ Push day            00:12:41  ⚙ │   day label, elapsed, working-set count
│ 7 / 21 working sets              │
├──────────────────────────────────┤
│ ▸ Bench press        3×8-10 80kg │   collapsed: done or not yet reached
│   ✓ 20×10  ✓ 40×5  ✓ 62.5×3      │
├──────────────────────────────────┤
│ ▾ Incline DB press               │   expanded: the current exercise
│   target 3 × 8-10  ·  rest 2-3m  │
│                                  │
│   warm-up                        │
│   ✓  12.5 × 10                   │   logged: compact, actual numbers
│   ▢  17.5 ×  5            [ Log ]│   pending: pre-filled, one tap
│                                  │
│   working                        │
│   ▢  27.5 ×  8   RIR 2    [ Log ]│
│   ▢  27.5 ×  8            [ Log ]│
│   ▢  27.5 ×  8            [ Log ]│
│   + add set                      │
├──────────────────────────────────┤
│         rest 1:47  ▇▇▇▇▇▁▁▁    ✕ │   sticky, only while running
└──────────────────────────────────┘
```

The exercise list is a vertical scroll of cards. Exactly one card is expanded — the first with
unlogged prescribed sets — and tapping any card expands it instead. When every prescribed set is
logged, the **last** exercise stays expanded rather than collapsing them all, so the screen never
shows a wall of closed cards with no obvious next action; the Main button then reads *Finish
workout*.

Tapping the weight or the reps on a pending row opens a stepper: weight steps by the profile's
`LoadingIncrement`, reps by 1. RIR is optional and collapsed until tapped. Tapping **Log** writes
the row.

### The row model is derived, never stored

`useSetRows(exercise)` computes the rows from two inputs the server already sends: the prescription
(`WorkingSetCount`, `SuggestedWarmup`, `SuggestedWorkingWeight`, `TargetRepMin`) and
`exercise.Sets`, the sets actually logged.

- Warm-up rows: one per `SuggestedWarmup` entry, in order.
- Working rows: `WorkingSetCount` of them, pre-filled at `SuggestedWorkingWeight × TargetRepMin`.
- Logged sets are matched onto rows by kind and ordinal, and a matched row renders as confirmed with
  the **actual** logged numbers, not the prescribed ones.
- Sets logged beyond the prescribed count — the backend accepts any number — render as extra
  confirmed rows after the prescribed ones. `+ add set` appends a pending row, so an athlete who
  gets a fourth set is not fighting the UI.

Deriving rather than storing means the screen is a pure function of the server response plus
in-flight mutations, and a refetch can never disagree with what is on screen.

Target reps pre-fill at `TargetRepMin` — the bottom of the range — because that is the number the
athlete is certain to hit, and adjusting up is one tap on a number they are looking at anyway.

---

## 7. Logging a set: optimistic writes and idempotency

This is the section the rest of the app exists to serve.

### The pending-id map

`POST /api/sessions/{id}/exercises/{exerciseId}/sets` takes a client-generated `ClientSetId`, and
the backend holds a unique index on it: replaying the same id returns the original set rather than
creating a second one. The client's whole duty is to **reuse the same id for the same logical set**.

`usePendingSetIds()` keeps a map from a stable row key —
`` `${sessionExerciseId}:${kind}:${ordinal}` `` — to a UUID, assigned lazily with
`crypto.randomUUID()` on the first tap and **not cleared until that row is confirmed logged**.

The subtle part, stated plainly because getting it wrong silently duplicates data:

- TanStack Query's automatic retries replay the *same* mutation variables, so automatic retries are
  safe no matter where the id is generated.
- A **manual re-tap** after a visible failure is the dangerous case. If the first request actually
  reached the server and only its response was lost, a freshly generated id would create a second
  set. Keying the id to the row rather than to the tap closes exactly this hole.
- The id must also survive a component remount, which is why the map lives in session-scoped
  context rather than component state.

### The mutation

On tap:

1. Look up or assign the row's `clientSetId`.
2. Patch `['session', sessionId]` in the query cache: append a `LoggedSet` carrying that
   `clientSetId` and a local `status: "pending"`.
3. **Start the rest timer** (§8). It does not wait for the server — the athlete is already racking
   the bar.
4. Fire the mutation.
5. On success, replace the optimistic entry **matched by `clientSetId`** with the server's
   `LoggedSetResponse`, and release the row's id from the map.
6. On final failure, mark the entry `status: "failed"` and leave it on screen with a *Retry*
   affordance. **It is not rolled back.** Removing a row the athlete already performed is worse than
   showing it as unsynced; they have the number in their head for about thirty seconds.

   One exception: `session.closed` means the session was completed or abandoned elsewhere, so the
   set can never be stored against it and *Retry* would fail forever. There the optimistic entry is
   dropped, the session is refetched, and the screen falls back to read-only (§10). This is the only
   code that discards an optimistic set, and it is discarded because retrying is impossible rather
   than because it failed.

Matching by `clientSetId` rather than by array position is what makes a concurrent background
refetch harmless: the server's copy and the optimistic copy are recognisably the same set.

### Retry policy

| Condition | Behaviour |
|---|---|
| Network error, 5xx | Retry 3×, exponential backoff from 500 ms |
| 429 | Retry honouring `Retry-After` |
| 401 | Refresh once, replay once (§4) |
| Other 4xx | No retry — the payload is wrong, repeating it cannot help |

Reads refetch on window focus and on reconnect; writes never refetch the whole session, because a
refetch racing an optimistic patch is how optimistic UIs flicker.

### What is not built

Writes survive a dropped connection **within a session** — they sit in the mutation cache and retry.
They do **not** survive a cold start: nothing is persisted to disk, so an app killed with a failed
set on screen loses that set. That is the documented limit of the "online-only with optimistic UI"
decision in the backend spec, and the honest statement of it is that the idempotency key makes
durable queueing a later addition rather than a rewrite.

---

## 8. The rest timer and the exercise catalog

**Rest timer.** Logging a set starts a countdown for that exercise's `RestMinSeconds`, which the API
returns on every session response, so no new endpoint is needed. The card displays the full
`RestMinSeconds`–`RestMaxSeconds` range as prescribed, but the countdown runs to the **minimum**:
it marks the earliest moment the next set is reasonable, not a deadline. It lives in
`RestTimerProvider` — above the router, so navigating does not kill it — and holds only a target
timestamp, not a decrementing counter: a `setInterval` that ticks a number drifts and stops in a
backgrounded webview, whereas `targetAt - Date.now()` is correct the instant the webview wakes.

At zero it fires `WebApp.HapticFeedback.notificationOccurred('success')` and nothing else. No audio —
the phone is in a pocket and the gym is loud — and no notification, which a Mini App cannot send
anyway. The timer is dismissable and advisory; it never blocks logging the next set.

**Exercise names.** Neither the preview nor the session response carries an exercise name, only
`ExerciseId`. `useExerciseNames()` loads `GET /api/exercises?lang=<Telegram language_code>` once per
launch with a long `staleTime` — the catalog is seeded, shared, and effectively immutable — and
exposes an id→name lookup. A row whose id is missing from the catalog renders a neutral placeholder
rather than throwing; an unnamed exercise is a cosmetic failure, and a crashed workout screen is
not.

---

## 9. Development harness

Two independent fakes, because two independent things are unavailable in a normal browser.

**MSW for the API.** Mock Service Worker intercepts at the network boundary, so `src/api/client.ts`
always speaks real HTTP and there is no mock-shaped code path in the production bundle and no
`if (import.meta.env.DEV)` branch in the data layer — the most common way a mocked frontend passes
its own tests and fails against the real server. Handlers live in `test/msw/handlers.ts` and are
shared by the dev server and the test suite. Fixtures are typed as the **generated** response types,
so a contract change breaks the fixtures at compile time.

MSW is opt-in per run (`VITE_USE_MSW=1`), not the default, so pointing the dev server at a real
local API stays a one-variable change.

**A fake Telegram runtime.** `window.Telegram.WebApp` does not exist outside Telegram, and
`initData` is empty there. Rather than scatter `if (inTelegram)` checks, the app depends on a
`TelegramRuntime` interface — `initData`, `themeParams`, `BackButton`, `MainButton`,
`HapticFeedback`, `expand`, `enableClosingConfirmation` — with two implementations: one delegating to
the real SDK, one a dev-only fake that renders the Back and Main buttons as real on-page controls so
the chrome wiring is actually exercised in the browser.

The fake supplies an `initData` string **signed with a development bot token**, so the real
HMAC-checked `/api/auth/telegram` path runs end to end in development. That is the difference
between faking the platform and bypassing authentication: the signature check always runs.

The fake is selected by `VITE_TELEGRAM_FAKE=1` and its module is excluded from the production build,
so it cannot be reached in a deployed bundle even by setting a flag.

**Real-device verification comes later**, over a tunnel to the local API, once PostgreSQL is running.
Until then the chrome is exercised against the fake, and that limit is stated rather than assumed
away.

---

## 10. Error handling

The API returns RFC 9457 ProblemDetails with an `errorCode` extension carrying a stable dotted code
(`session.closed`, `program.not_found`, …). `src/api/client.ts` parses every non-2xx body into an
`ApiError { status, errorCode, title }`.

**The client branches on `errorCode`, never on `title`** — `title` is a human-readable message and
may be reworded — and never on status alone, because one status covers many codes: 409 alone carries
`session.already_in_progress`, `session.closed`, `program.already_active`, `program.archived`,
`program.not_editable`, `program.edit_conflict`, `session.needs_at_least_one_working_set`,
`program.needs_at_least_one_day`, and `program.day_needs_at_least_one_prescription`.

`src/errors/codes.ts` holds one table from code to user-facing message and recovery action. Codes
that need behaviour rather than a message:

| Code | Behaviour |
|---|---|
| `session.already_in_progress` | Starting a day while another session is live. Fetch `/current` and navigate to it rather than showing an error — the athlete wants to be in a workout. |
| `session.closed` | The session was completed or abandoned elsewhere. Refetch and fall back to read-only. |
| `session.needs_at_least_one_working_set` | *Finish* on a session with no working set. Explain and keep the session open. |
| `auth.refresh_token_invalid` | Re-exchange `initData` (§4). |
| `*.not_found` | The resource is gone. Navigate to `/` with a message, rather than leaving a dead screen. |

Unknown codes fall through to a generic message plus *Try again*. An unmapped code must degrade, not
crash: the backend will gain codes this client has never seen.

Rendering errors are caught by one route-level error boundary whose fallback offers *Reload*, so a
crash in one screen cannot blank the app mid-workout.

---

## 11. Testing

| Tier | Tool | What it proves |
|---|---|---|
| Unit | Vitest | `useSetRows` row derivation; the pending-id map's reuse rule; timer maths across a backgrounded gap |
| Component | Vitest + Testing Library + MSW | A tap logs a set and the row confirms; a failed write stays on screen as `failed`; a re-tap reuses the same `clientSetId`; the rest timer starts on log |
| Contract | `tsc --noEmit` + CI | Fixtures typecheck against generated types; regenerating `openapi.json` and `generated.ts` produces no diff (a CI step, not a test case — it needs the .NET SDK) |
| Chrome | Vitest | Back/Main button handlers subscribe and unsubscribe; no stale handler survives unmount |

The three tests that matter most, because they encode the design decisions most expensive to get
wrong:

1. **A re-tap after a failure reuses the `clientSetId`.** Asserted against the recorded request, not
   against internal state. This is the test that stops duplicate sets.
2. **A failed write is not rolled back.** The row stays, marked unsynced.
3. **The timer is correct after a simulated background gap**, by advancing the clock rather than
   waiting.

No end-to-end browser tier in v1. It would test the fake Telegram runtime, not Telegram, and the
real verification is a phone against a tunnel.

---

## 12. Build and deployment

`npm run build` emits static files to `clients/mini-app/dist`. Caddy serves them on the **same
origin** as the API — the API under `/api`, the client everywhere else — which keeps CORS
unconfigured and unnecessary (item 4 on the backend's follow-up list resolves to "not needed", and
this design is why). `VITE_API_BASE_URL` defaults to `/api` for exactly that reason.

Telegram requires HTTPS for Mini Apps, which `MiniApp:Url` already validates at API startup.

CI: `npm ci`, `tsc --noEmit`, `vitest run`, `npm run build`, plus the regeneration check from §3.

---

## 13. Decisions recorded

Settled with the user before this document:

1. **Logger-first.** Program authoring is deferred; the athlete's program is seeded through the API.
2. **Pre-filled, one tap to confirm.** The screen shows the weight and reps it expects; one tap
   accepts, tapping a number adjusts.
3. **Mock both the API and the Telegram runtime**; verify on a device later.
4. **The rest timer starts itself** when a set is logged.
5. **Approach A**: Vite + React + TanStack Query, over Next.js (a server that buys nothing in a
   webview) and over Preact-plus-handwritten-state (saves ~15 KB by hand-writing the machinery that
   protects logged sets).
6. **An OpenAPI document is worth adding to the backend**, which turned out to be more than the
   two lines advertised — response types had to be declared per endpoint and the Telegram webhook
   excluded. Done in `17d7e33`.

## 14. Known limits

Stated rather than discovered later:

1. **No write durability across a cold start** (§7). The idempotency key makes this an addition, not
   a rewrite.
2. **Real-device verification is outstanding**, and so is anything that depends on PostgreSQL: the
   backend's ~52 integration tests have still never run (see `docs/superpowers/HANDOFF.md`).
3. **Refresh-token rows grow per app launch** (§4), which sharpens an existing backend follow-up.
4. **`RevisionNumber` is displayed nowhere in v1.** The backend increments it by tens per logical
   edit (follow-up item 10), so showing it would be showing a meaningless number.
5. **The exercise catalog has no filters.** `GET /api/exercises?muscle=&equipment=` is specified in
   the backend spec and unimplemented (follow-up item 6). v1 only needs id→name, so this does not
   block, but an authoring UI will need it.
