# MyGym Mini App

The Telegram Mini App client for the workout logger. React 19 + TypeScript, built with Vite to
static files.

**Design:** `docs/superpowers/specs/2026-11-23-mygym-mini-app-design.md`
**Plan:** `docs/superpowers/plans/2026-11-23-mini-app-frontend.md`

## What it does

Sign in from Telegram with no visible login step, show the day you are due to train, log each set
with one tap on a row that already shows the weight and reps you are expected to hit, start a rest
countdown automatically, finish, and look back over past workouts.

Program **authoring** is deliberately not here. The program is created through the API; a UI for
building one is its own piece of work.

## Running it

### Against mocks, in an ordinary browser

```bash
npm install
VITE_USE_MSW=1 VITE_TELEGRAM_FAKE=1 npm run dev
```

Two independent fakes, because two independent things are missing outside Telegram:

- **`VITE_USE_MSW=1`** makes Mock Service Worker answer the API from fixtures. It intercepts at the
  network boundary, so the app always speaks real HTTP and there is no mock-shaped code path in the
  production bundle.
- **`VITE_TELEGRAM_FAKE=1`** supplies a fake `TelegramRuntime`. `window.Telegram.WebApp` does not
  exist in a normal browser, so without this the app refuses to start and says so.

### Against a real local API

```bash
# From the repository root, with PostgreSQL running:
dotnet run --project src/Host/MyGym.Api

# Here:
VITE_TELEGRAM_FAKE=1 VITE_DEV_BOT_TOKEN='<your dev bot token>' npm run dev
```

The dev server proxies `/api` to `http://127.0.0.1:5199`.

`VITE_DEV_BOT_TOKEN` matters: the fake signs a real `initData` string with it, using the same HMAC
derivation the backend validates, so the **signature-checked** auth path runs end to end. Without
the token `initData` is left empty and sign-in fails with a clear configuration error rather than a
confusing signature one.

There is deliberately **no development auth bypass**. A flag that minted a token without a
signature check would be a production authentication bypass one misconfiguration away from being
live, and would leave the real path never exercised.

## Regenerating the API contract

`openapi.json` and `src/api/generated.ts` are both committed, so the client typechecks without a
.NET SDK, a database, or a running API. To refresh them after a backend change:

```bash
# From the repository root — needs the .NET SDK but no database:
bash scripts/dump-openapi.sh clients/mini-app/openapi.json

# Here:
npm run api:types
```

CI runs exactly this and fails on any diff, so a stale contract is a red build rather than a
runtime surprise. **Never hand-edit `src/api/generated.ts`**, and never declare an API shape by
hand anywhere in `src/` — the point of the generated types is that a server-side rename becomes a
compile error here.

## Scripts

| Script | What it does |
|---|---|
| `npm run dev` | Dev server on port 5173 |
| `npm run verify` | Typecheck then the full test suite — run this before committing |
| `npm run typecheck` | `tsc --noEmit` |
| `npm test` | Vitest, once |
| `npm run test:watch` | Vitest, watching |
| `npm run build` | Typecheck then a production build into `dist/` |
| `npm run api:types` | Regenerate `src/api/generated.ts` from `openapi.json` |

## How it is put together

```
src/
  api/          generated types, the fetch client, query keys, queries, mutations
  auth/         initData -> tokens, held in memory only
  telegram/     the TelegramRuntime interface, the real one, the fake, and the chrome hooks
  session/      the logging screen, the row model, the idempotency map, the rest timer
  today/        the active program and today's day
  preview/      a day as prescribed, with Start
  history/      past workouts
  profile/      unit, plate increment, bodyweight
  errors/       the errorCode -> message and recovery table
  ui/           Stepper, Spinner, ErrorBanner, EmptyState
test/msw/       request handlers and fixtures, shared by the dev server and the tests
```

Three things are worth knowing before changing anything:

**Set logging is idempotent, and that is load-bearing.** The backend holds a unique index on
`clientSetId`, so replaying a set-log returns the original rather than creating a second set. The
client's duty is to reuse the same id for the same logical set, which is why the id is keyed to the
row and held above the router — see `session/PendingSetIdsProvider.tsx`. Minting an id at the tap
instead would duplicate a set whenever a response was lost and the athlete tapped again.

**A failed write is never rolled back.** The row stays on screen marked unsynced, with Retry. The
only exception is `session.closed`, where the set can never be stored and a Retry affordance would
lie.

**The rest timer stores a target timestamp, not a remaining count.** A phone in a pocket suspends
the webview, so a decrementing counter would simply stop.

## Telegram's chrome

The Back and Main buttons are single global objects, not per-screen widgets, so a handler left
bound after its screen unmounts fires a stale action. `useBackButton` and `useMainButton` are their
only owners and both clean up on unmount; nothing outside `src/telegram/` touches
`window.Telegram`.

`useClosingConfirmation` is enabled only while a workout is in progress, so a swipe-down
mid-workout cannot silently close the app.

## Known gaps

1. **No real-device verification yet.** Everything here has been exercised against the fake runtime
   and mocked API. A pass inside real Telegram, over a tunnel to a real API, is still outstanding —
   and is blocked on the same thing the backend's integration tests are: PostgreSQL cannot start on
   the development machine (see `docs/superpowers/HANDOFF.md`).
2. **Writes do not survive a cold start.** A failed set sits in the mutation cache and retries
   while the app is open, but nothing is persisted to disk. The idempotency key makes durable
   queueing a later addition rather than a rewrite.
3. **Render errors go only to the console.** There is no client-error endpoint on the backend, and
   inventing a contract for one here would be guessing.
4. **No end-to-end browser tier.** It would test the fake Telegram runtime rather than Telegram.
   The real verification is gap 1.
