# MyGym Mini App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Telegram Mini App client for the MyGym workout logger — sign in from the
Telegram runtime, see today's day, log a workout with one tap per pre-filled set, finish, review
history.

**Architecture:** Vite + React 19 + TypeScript static SPA inside a Telegram webview. TanStack Query
owns all server state; its optimistic-mutation and retry machinery is paired with the backend's
`ClientSetId` idempotency key so a dropped connection can never duplicate or lose a logged set.
Client state is two contexts: the rest timer and the pending-set-id map. All API types are generated
from the backend's OpenAPI document, never hand-written.

**Tech Stack:** React 19.3.1, TypeScript 7.0.2, Vite 8.3.8, TanStack Query 5.108.3,
react-router 8.5.2, MSW 3.0.7, Vitest 5.1.1, Testing Library 16.3.3, openapi-typescript 7.13.0,
@telegram-apps/sdk 3.11.8.

**Spec:** `docs/superpowers/specs/2026-11-23-mygym-mini-app-design.md`

**On the density of this plan:** load-bearing pure logic (row derivation, the idempotency-key map,
the optimistic mutation, timer maths) is given as complete code with complete tests, because those
are the places where a wrong implementation is invisible until it corrupts a workout. Presentational
components are specified by their props, their rendered content, and their test assertions rather
than transcribed line by line, because their correctness is visible immediately and their exact
markup is not load-bearing. Every task still ends with a running test.

---

## Global Constraints

- **Working directory for all frontend work is `clients/mini-app/`.** Never add Node tooling to the
  repository root.
- **Exact dependency versions, pinned, no carets.** React 19.3.1, react-dom 19.3.1,
  typescript 7.0.2, vite 8.3.8, @vitejs/plugin-react 6.3.0, @tanstack/react-query 5.108.3,
  react-router 8.5.2, msw 3.0.7, vitest 5.1.1, @testing-library/react 16.3.3,
  @testing-library/user-event 14.6.13, jsdom 31.0.1, openapi-typescript 7.13.0,
  @telegram-apps/sdk 3.11.8. TypeScript 7 is the Go-based compiler and is new; if a tool proves
  incompatible with it, fall back to typescript 6.0.3 and say so in the task report.
- **`src/api/generated.ts` is generated and MUST NOT be hand-edited.** No file under `src/` may
  declare an API request or response shape by hand. Import from `generated.ts`.
- **The client branches on `errorCode`, never on ProblemDetails `title`, and never on HTTP status
  alone.** One status covers many codes.
- **No development authentication bypass.** The real signature-checked `/api/auth/telegram` path
  always runs. Development fakes the Telegram *runtime*, not authentication.
- **No `localStorage`, `sessionStorage`, or cookie persistence of tokens.** Access and refresh
  tokens live in memory only; a cold start re-exchanges `initData`.
- **Nothing outside `src/telegram/` may touch `window.Telegram`.** Components depend on the
  `TelegramRuntime` interface.
- **Tests use Vitest + Testing Library + MSW.** No snapshot tests. No test that asserts nothing.
- **Strict TypeScript**: `strict: true`, `noUncheckedIndexedAccess: true`, `exactOptionalPropertyTypes: true`.
- **Commit after every task** with a `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>` trailer.
- The backend API is at `/api` on the same origin in production; `VITE_API_BASE_URL` defaults to
  `/api`.
- **PostgreSQL is unavailable on this machine** (see `docs/superpowers/HANDOFF.md`). No task may
  depend on a running API or database. `clients/mini-app/openapi.json` is committed in Task 1 and is
  the contract for every later task.

---

## File Structure

| File | Responsibility |
|---|---|
| `clients/mini-app/package.json` | Dependencies, scripts (`dev`, `build`, `test`, `typecheck`, `api:types`) |
| `clients/mini-app/vite.config.ts` | React plugin, dev proxy to the API, Vitest config |
| `clients/mini-app/tsconfig.json` | Strict compiler options |
| `clients/mini-app/openapi.json` | Committed copy of the backend contract |
| `clients/mini-app/src/api/generated.ts` | Generated types — never hand-edited |
| `clients/mini-app/src/api/client.ts` | `fetch` wrapper: base URL, bearer token, ProblemDetails → `ApiError` |
| `clients/mini-app/src/api/queries.ts` | Query keys and `useQuery` hooks |
| `clients/mini-app/src/api/mutations.ts` | `useMutation` hooks including the optimistic set log |
| `clients/mini-app/src/errors/codes.ts` | `errorCode` → message and recovery action |
| `clients/mini-app/src/telegram/runtime.ts` | `TelegramRuntime` interface + real SDK implementation |
| `clients/mini-app/src/telegram/fake-runtime.ts` | Dev-only fake with a signed `initData` |
| `clients/mini-app/src/telegram/useBackButton.ts` | Back button ↔ history depth |
| `clients/mini-app/src/telegram/useMainButton.ts` | Sole owner of `MainButton` |
| `clients/mini-app/src/telegram/useThemeParams.ts` | `themeParams` → CSS custom properties |
| `clients/mini-app/src/auth/AuthProvider.tsx` | `initData` → tokens, in-memory, refresh on 401 |
| `clients/mini-app/src/session/useSetRows.ts` | Derives the row model from prescription + logged sets |
| `clients/mini-app/src/session/usePendingSetIds.tsx` | The idempotency-key map |
| `clients/mini-app/src/session/RestTimerProvider.tsx` | Target-timestamp countdown, above the router |
| `clients/mini-app/src/session/SessionScreen.tsx` | Live and read-only session |
| `clients/mini-app/src/session/ExerciseCard.tsx` | One exercise, expanded or collapsed |
| `clients/mini-app/src/session/SetRow.tsx` | One set: pending, pending-with-stepper, logged, failed |
| `clients/mini-app/src/today/TodayScreen.tsx` | Active program, today's day, resume |
| `clients/mini-app/src/preview/DayPreviewScreen.tsx` | The day as prescribed, with Start |
| `clients/mini-app/src/history/HistoryScreen.tsx` | Past sessions, paged |
| `clients/mini-app/src/profile/ProfileScreen.tsx` | Unit, increment, bodyweight |
| `clients/mini-app/src/catalog/useExerciseNames.ts` | id → name lookup |
| `clients/mini-app/src/ui/*` | `Button`, `Stepper`, `Spinner`, `ErrorBanner` |
| `clients/mini-app/src/routes.tsx` | The route table |
| `clients/mini-app/src/App.tsx` | Providers + router |
| `clients/mini-app/test/msw/handlers.ts` | Request handlers shared by dev server and tests |
| `clients/mini-app/test/msw/fixtures.ts` | Fixtures typed as generated response types |

---

### Task 1: Scaffold, tooling, and the generated contract

**Files:**
- Create: `clients/mini-app/package.json`, `tsconfig.json`, `tsconfig.node.json`, `vite.config.ts`,
  `index.html`, `.gitignore`, `src/main.tsx`, `src/App.tsx`, `src/vite-env.d.ts`
- Create: `clients/mini-app/openapi.json` (copy of the generated document)
- Create: `clients/mini-app/src/api/generated.ts` (generated)
- Create: `clients/mini-app/test/setup.ts`, `clients/mini-app/src/sanity.test.ts`
- Modify: `.gitattributes` (add frontend text-file rules)

**Interfaces:**
- Consumes: `scripts/dump-openapi.sh` from the repo root, and the API source at
  `src/Host/MyGym.Api` (needs the .NET SDK, no database).
- Produces: `src/api/generated.ts` exporting `components["schemas"][...]`. Later tasks import
  response types from it, aliased in each task as shown.

- [ ] **Step 1: Create the project directory and `package.json`**

```jsonc
// clients/mini-app/package.json
{
  "name": "mygym-mini-app",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc --noEmit && vite build",
    "preview": "vite preview",
    "typecheck": "tsc --noEmit",
    "test": "vitest run",
    "test:watch": "vitest",
    "api:types": "openapi-typescript openapi.json -o src/api/generated.ts"
  },
  "dependencies": {
    "@tanstack/react-query": "5.108.3",
    "@telegram-apps/sdk": "3.11.8",
    "react": "19.3.1",
    "react-dom": "19.3.1",
    "react-router": "8.5.2"
  },
  "devDependencies": {
    "@testing-library/react": "16.3.3",
    "@testing-library/user-event": "14.6.13",
    "@types/react": "19.3.1",
    "@types/react-dom": "19.3.1",
    "@vitejs/plugin-react": "6.3.0",
    "jsdom": "31.0.1",
    "msw": "3.0.7",
    "openapi-typescript": "7.13.0",
    "typescript": "7.0.2",
    "vite": "8.3.8",
    "vitest": "5.1.1"
  },
  "msw": { "workerDirectory": ["public"] }
}
```

If `@types/react` 19.3.1 does not exist, use the latest `19.x` and note the resolved version in the
task report.

- [ ] **Step 2: Write `tsconfig.json`**

```jsonc
// clients/mini-app/tsconfig.json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "jsx": "react-jsx",
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "exactOptionalPropertyTypes": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "verbatimModuleSyntax": true,
    "skipLibCheck": true,
    "noEmit": true,
    "isolatedModules": true,
    "types": ["vite/client", "vitest/globals"]
  },
  "include": ["src", "test"]
}
```

- [ ] **Step 3: Write `vite.config.ts`**

The dev proxy forwards `/api` to the local .NET host so the real auth path can run when MSW is off.
`server.allowedHosts` is needed for tunnelled real-device testing later.

```ts
// clients/mini-app/vite.config.ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    allowedHosts: true,
    proxy: { "/api": { target: "http://127.0.0.1:5199", changeOrigin: true } },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./test/setup.ts"],
    css: false,
  },
});
```

- [ ] **Step 4: Write `index.html`, `src/main.tsx`, `src/App.tsx`, `src/vite-env.d.ts`**

`App.tsx` is a placeholder returning `<div>MyGym</div>`; Task 13 replaces it. `vite-env.d.ts`
declares the env vars used across the app:

```ts
// clients/mini-app/src/vite-env.d.ts
/// <reference types="vite/client" />
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
  readonly VITE_USE_MSW?: string;
  readonly VITE_TELEGRAM_FAKE?: string;
  readonly VITE_DEV_BOT_TOKEN?: string;
}
interface ImportMeta { readonly env: ImportMetaEnv }
```

- [ ] **Step 5: Generate `openapi.json` and `src/api/generated.ts`**

From the repository root:

```bash
bash scripts/dump-openapi.sh clients/mini-app/openapi.json
cd clients/mini-app && npm install && npm run api:types
```

Expected: `openapi.json` is ~48 KB with 18 paths and 33 schemas; `src/api/generated.ts` exports
`paths` and `components`.

- [ ] **Step 6: Verify the contract contains the types later tasks need**

Write `src/sanity.test.ts` asserting the generated types exist and have the fields the design
depends on. This is a real test, not a smoke test: it fails if the backend contract drifts.

```ts
// clients/mini-app/src/sanity.test.ts
import { expect, test } from "vitest";
import type { components } from "./api/generated";

type SessionResponse = components["schemas"]["SessionResponse"];
type SessionExercise = components["schemas"]["SessionExerciseResponse"];
type LoggedSet = components["schemas"]["LoggedSetResponse"];
type DayPreview = components["schemas"]["DayPreviewResponse"];

test("the generated contract carries the fields the session screen renders from", () => {
  // Compile-time assertions: these fail `tsc` if the backend renames a field.
  const exercise: SessionExercise = {
    id: "", order: 0, exerciseId: "", alternativeExerciseId: null,
    workingSetCount: 3, targetRepMin: 8, targetRepMax: 10,
    restMinSeconds: 120, restMaxSeconds: 180,
    suggestedWarmup: [], suggestedWorkingWeight: 80, sets: [],
  };
  const set: LoggedSet = {
    id: "", order: 0, kind: "Working", weight: 80, unit: "Kilograms",
    reps: 8, rir: 2, clientSetId: "", loggedAt: "",
  };
  expect(exercise.workingSetCount).toBe(3);
  expect(set.clientSetId).toBe("");
  // Runtime assertion that the two response types are distinguishable at the type level.
  const session: SessionResponse = {
    id: "", status: "InProgress", startedAt: "", completedAt: null,
    programId: null, programRevision: null, programDayId: null,
    bodyweightAtSession: null, workingSetCount: 0, exercises: [],
  };
  expect(session.exercises).toHaveLength(0);
  const preview: DayPreview = {
    programId: "", dayId: "", label: "", revisionNumber: 1, exercises: [],
  };
  expect(preview.revisionNumber).toBe(1);
});
```

The generated shapes were verified before this plan was written: camelCase properties, string-union
enums, plain `number` for decimals and ints with `| null` where nullable. If any of that has changed,
the **generated file is the authority** — correct the test to match it and record the change in the
task report, because every later task's field accesses depend on it.

- [ ] **Step 7: Run typecheck and tests**

```bash
cd clients/mini-app && npm run typecheck && npm test
```

Expected: both pass.

- [ ] **Step 8: Add `.gitignore` and extend `.gitattributes`**

`clients/mini-app/.gitignore`: `node_modules/`, `dist/`, `public/mockServiceWorker.js`.
Append to the repository-root `.gitattributes`:

```
*.ts  text eol=lf
*.tsx text eol=lf
*.json text eol=lf
```

- [ ] **Step 9: Commit**

```bash
git add clients/mini-app .gitattributes
git commit -m "feat(mini-app): scaffold the Vite + React client with generated API types

Types are generated from the backend's committed OpenAPI document, so a
server-side rename is a client compile error rather than a runtime surprise.
openapi.json and generated.ts are both committed so the client typechecks
without a .NET SDK or a database.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: The API client and the error-code table

**Files:**
- Create: `clients/mini-app/src/api/client.ts`
- Create: `clients/mini-app/src/api/ApiError.ts`
- Create: `clients/mini-app/src/errors/codes.ts`
- Test: `clients/mini-app/src/api/client.test.ts`, `clients/mini-app/src/errors/codes.test.ts`

**Interfaces:**
- Consumes: nothing from earlier tasks except `generated.ts`.
- Produces:
  - `class ApiError extends Error { status: number; errorCode: string | null; title: string }`
  - `type TokenSource = () => string | null`
  - `function createApiClient(opts: { baseUrl: string; getToken: TokenSource; onUnauthorized: () => Promise<boolean> }): ApiClient`
  - `interface ApiClient { request<T>(path: string, init?: RequestInit & { json?: unknown }): Promise<T> }`
  - `function messageForCode(code: string | null): string`
  - `type RecoveryAction = "none" | "goHome" | "resumeCurrentSession" | "refetchReadOnly" | "reauthenticate"`
  - `function recoveryForCode(code: string | null): RecoveryAction`

- [ ] **Step 1: Write the failing tests for `ApiError` parsing**

```ts
// clients/mini-app/src/api/client.test.ts
import { beforeEach, describe, expect, test, vi } from "vitest";
import { createApiClient } from "./client";
import { ApiError } from "./ApiError";

function jsonResponse(status: number, body: unknown, contentType = "application/problem+json") {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": contentType } });
}

describe("createApiClient", () => {
  beforeEach(() => vi.restoreAllMocks());

  test("parses a ProblemDetails body into ApiError with its errorCode", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(409, {
      title: "A workout is already in progress.",
      status: 409,
      type: "https://mygym.app/errors/session.already_in_progress",
      errorCode: "session.already_in_progress",
    })));
    const client = createApiClient({ baseUrl: "/api", getToken: () => "t", onUnauthorized: async () => false });

    const err = await client.request("/sessions").catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).errorCode).toBe("session.already_in_progress");
    expect((err as ApiError).status).toBe(409);
  });

  test("survives a non-2xx with a body that is not JSON", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(
      new Response("<html>502</html>", { status: 502, headers: { "content-type": "text/html" } })));
    const client = createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: async () => false });

    const err = await client.request("/sessions").catch((e: unknown) => e) as ApiError;
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(502);
    expect(err.errorCode).toBeNull();
  });

  test("returns null for 204 rather than throwing on an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    const client = createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: async () => false });
    await expect(client.request("/sessions/current")).resolves.toBeNull();
  });

  test("sends the bearer token when one is available and omits the header when not", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, {}, "application/json"));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient({ baseUrl: "/api", getToken: () => "abc", onUnauthorized: async () => false })
      .request("/me");
    let headers = new Headers((fetchMock.mock.calls[0]![1] as RequestInit).headers);
    expect(headers.get("authorization")).toBe("Bearer abc");

    await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: async () => false })
      .request("/me");
    headers = new Headers((fetchMock.mock.calls[1]![1] as RequestInit).headers);
    expect(headers.has("authorization")).toBe(false);
  });

  test("on 401 asks onUnauthorized to refresh and replays the request exactly once", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { errorCode: "auth.refresh_token_invalid", title: "x" }))
      .mockResolvedValueOnce(jsonResponse(200, { id: "s1" }, "application/json"));
    vi.stubGlobal("fetch", fetchMock);
    const onUnauthorized = vi.fn().mockResolvedValue(true);

    const result = await createApiClient({ baseUrl: "/api", getToken: () => "t", onUnauthorized })
      .request<{ id: string }>("/sessions/s1");

    expect(result).toEqual({ id: "s1" });
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  test("does not replay a second time when the refresh itself fails", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(401, { errorCode: "auth.refresh_token_invalid", title: "x" }));
    vi.stubGlobal("fetch", fetchMock);
    const onUnauthorized = vi.fn().mockResolvedValue(false);

    await expect(createApiClient({ baseUrl: "/api", getToken: () => "t", onUnauthorized })
      .request("/sessions")).rejects.toBeInstanceOf(ApiError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  test("serialises a json body and sets content-type", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, {}, "application/json"));
    vi.stubGlobal("fetch", fetchMock);
    await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: async () => false })
      .request("/sessions", { method: "POST", json: { programId: "p1" } });

    const init = fetchMock.mock.calls[0]![1] as RequestInit;
    expect(init.body).toBe(JSON.stringify({ programId: "p1" }));
    expect(new Headers(init.headers).get("content-type")).toBe("application/json");
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd clients/mini-app && npx vitest run src/api/client.test.ts
```

Expected: FAIL — `Cannot find module './client'`.

- [ ] **Step 3: Implement `ApiError.ts` and `client.ts`**

`ApiError` carries `status`, `errorCode` (the ProblemDetails `errorCode` extension, or `null`), and
`title`. `createApiClient` returns an object with `request<T>`; it joins `baseUrl` and `path`,
attaches `Authorization` when `getToken()` is non-null, serialises `init.json`, returns `null` for
204 and for an empty body, parses `application/problem+json` and `application/json` error bodies for
`errorCode`, and falls back to `errorCode: null` when the body is unparseable. A 401 calls
`onUnauthorized()` once; if it resolves `true` the request is replayed once with a freshly read
token, and the replay's own 401 is thrown rather than refreshed again. Guard the replay with a
boolean local, not a retry counter, so there is exactly one.

- [ ] **Step 4: Run the tests to verify they pass**

Expected: 7 passing.

- [ ] **Step 5: Write the failing tests for the error-code table**

```ts
// clients/mini-app/src/errors/codes.test.ts
import { expect, test } from "vitest";
import { messageForCode, recoveryForCode } from "./codes";

test("maps the codes that drive behaviour rather than only a message", () => {
  expect(recoveryForCode("session.already_in_progress")).toBe("resumeCurrentSession");
  expect(recoveryForCode("session.closed")).toBe("refetchReadOnly");
  expect(recoveryForCode("auth.refresh_token_invalid")).toBe("reauthenticate");
  expect(recoveryForCode("session.not_found")).toBe("goHome");
  expect(recoveryForCode("program.not_found")).toBe("goHome");
  expect(recoveryForCode("session.needs_at_least_one_working_set")).toBe("none");
});

test("degrades for a code it has never seen rather than throwing", () => {
  expect(recoveryForCode("brand.new.code.from.the.future")).toBe("none");
  expect(messageForCode("brand.new.code.from.the.future")).toMatch(/try again/i);
  expect(messageForCode(null)).toMatch(/try again/i);
});

test("gives a specific, non-generic message to the codes an athlete will actually hit", () => {
  const generic = messageForCode("unknown");
  for (const code of [
    "session.already_in_progress",
    "session.closed",
    "session.needs_at_least_one_working_set",
    "set.reps_out_of_bounds",
    "set.rir_out_of_bounds",
  ]) {
    expect(messageForCode(code), code).not.toBe(generic);
  }
});
```

- [ ] **Step 6: Run to verify failure, then implement `codes.ts`**

A single `Record<string, { message: string; recovery: RecoveryAction }>` keyed by the backend's
stable codes, with `messageForCode`/`recoveryForCode` falling back to a generic message and
`"none"`. Cover at minimum every code in the table below; others may fall through to generic.

| Code | Recovery | Message intent |
|---|---|---|
| `session.already_in_progress` | `resumeCurrentSession` | A workout is already open — take me to it |
| `session.closed` | `refetchReadOnly` | This workout was already finished |
| `session.needs_at_least_one_working_set` | `none` | Log at least one working set before finishing |
| `session.not_found`, `program.not_found`, `program.day_not_found`, `session.exercise_not_found`, `set.not_found`, `exercise.not_found` | `goHome` | That is no longer there |
| `auth.refresh_token_invalid`, `auth.init_data_expired`, `auth.init_data_signature_invalid` | `reauthenticate` | Signing you in again |
| `set.reps_out_of_bounds`, `set.rir_out_of_bounds` | `none` | That number is out of range |
| `profile.time_zone_unknown`, `profile.weight_unit_invalid` | `none` | Field-specific |

- [ ] **Step 7: Run both test files**

Expected: all passing. Then `npm run typecheck`.

- [ ] **Step 8: Commit**

```bash
git add clients/mini-app
git commit -m "feat(mini-app): API client with ProblemDetails parsing and the error-code table

The client branches on the backend's stable errorCode extension, never on the
human-readable title and never on status alone -- 409 alone carries nine
distinct codes. Unknown codes degrade to a generic message, because the backend
will gain codes this client has never seen.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The MSW harness and typed fixtures

**Files:**
- Create: `clients/mini-app/test/msw/fixtures.ts`, `clients/mini-app/test/msw/handlers.ts`,
  `clients/mini-app/test/msw/server.ts`, `clients/mini-app/test/msw/browser.ts`
- Modify: `clients/mini-app/test/setup.ts`
- Test: `clients/mini-app/test/msw/handlers.test.ts`

**Interfaces:**
- Consumes: `components["schemas"]` from Task 1.
- Produces:
  - `fixtures`: `{ profile, exercises, program, dayPreview, session, sessionSummaries }`, each typed
    as the corresponding generated response type.
  - `handlers`: an MSW `RequestHandler[]` covering every endpoint the client calls.
  - `server` (node, for tests) and `worker` (browser, for `VITE_USE_MSW=1`).
  - `resetFixtureState()` — clears the in-memory session store between tests.

- [ ] **Step 1: Write the fixtures, typed as generated response types**

Fixtures are **typed as the generated types, not as structural literals**, so a contract change
breaks them at compile time. Use the athlete's real program so the fixtures are recognisable:
`"Push day"` with Bench press (3 × 8–10 @ 80, warm-up 27/53/80 % resolving to 20/40/62.5 on a 2.5 kg
increment) and Incline DB press (3 × 8–10 @ 27.5). Fixed UUIDs as string constants, exported, so
tests can address them.

The warm-up weights must be exactly `[20, 40, 62.5]` — these are the backend's own round-**down**
values, proven in `tests/MyGym.IntegrationTests/SessionEndpointTests.cs:79`. Do not recompute them;
copy them.

- [ ] **Step 2: Write the handlers**

A module-level mutable store holds the in-progress session so that `POST .../sets` mutates what
`GET /api/sessions/{id}` subsequently returns — otherwise optimistic-update tests prove nothing.

Handlers required:

| Method | Path | Behaviour |
|---|---|---|
| POST | `/api/auth/telegram` | 200 `AuthTokensResponse` when `initData` is non-empty; 401 `auth.init_data_signature_invalid` when it is `"invalid"` |
| POST | `/api/auth/refresh` | 200 with new tokens; 401 when the token is `"expired"` |
| GET | `/api/me` | 200 `ProfileResponse` |
| PATCH | `/api/me` | 200 with the merged profile |
| GET | `/api/exercises` | 200 `ExerciseSummary[]` |
| GET | `/api/programs` | 200 `ProgramSummaryResponse[]` |
| GET | `/api/programs/:id` | 200 `ProgramResponse` |
| GET | `/api/programs/:id/days/:dayId/preview` | 200 `DayPreviewResponse` |
| GET | `/api/sessions/current` | 200 when a session is open in the store, else **204** |
| POST | `/api/sessions` | 201 `SessionResponse`; 409 `session.already_in_progress` when one is open |
| GET | `/api/sessions/:id` | 200 from the store, 404 `session.not_found` otherwise |
| POST | `/api/sessions/:id/exercises/:exerciseId/sets` | 201 `LoggedSetResponse`; **if `clientSetId` already exists, return the existing set with 201 and do not append** |
| PATCH / DELETE | `.../sets/:setId` | 204 |
| POST | `/api/sessions/:id/complete` | 204; 409 `session.needs_at_least_one_working_set` when no working set is logged |
| POST | `/api/sessions/:id/abandon` | 204 |
| GET | `/api/sessions` | 200 `SessionSummaryResponse[]` |

The idempotent-replay behaviour in the sets handler is the single most important line in this task:
it is what makes Task 8's re-tap test meaningful.

- [ ] **Step 3: Write a test that the handlers honour idempotency and 204**

```ts
// clients/mini-app/test/msw/handlers.test.ts
import { afterAll, afterEach, beforeAll, beforeEach, expect, test } from "vitest";
import { server } from "./server";
import { resetFixtureState, IDS } from "./fixtures";

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
beforeEach(() => resetFixtureState());

test("GET /api/sessions/current is 204 with no open session and 200 once one is started", async () => {
  expect((await fetch("/api/sessions/current")).status).toBe(204);
  await fetch("/api/sessions", { method: "POST", body: JSON.stringify({ programId: IDS.programId, programDayId: IDS.dayId }), headers: { "content-type": "application/json" } });
  expect((await fetch("/api/sessions/current")).status).toBe(200);
});

test("replaying a set with the same clientSetId returns the original and does not append", async () => {
  const start = await (await fetch("/api/sessions", { method: "POST", body: JSON.stringify({}), headers: { "content-type": "application/json" } })).json();
  const exerciseId = start.exercises[0].id;
  const body = JSON.stringify({ clientSetId: "11111111-1111-1111-1111-111111111111", kind: "Working", weight: 80, reps: 8, rir: 2 });
  const url = `/api/sessions/${start.id}/exercises/${exerciseId}/sets`;

  const first = await (await fetch(url, { method: "POST", body, headers: { "content-type": "application/json" } })).json();
  const second = await (await fetch(url, { method: "POST", body, headers: { "content-type": "application/json" } })).json();

  expect(second.id).toBe(first.id);
  const session = await (await fetch(`/api/sessions/${start.id}`)).json();
  expect(session.exercises[0].sets).toHaveLength(1);
});
```

- [ ] **Step 4: Run, implement until green, wire `test/setup.ts`**

`test/setup.ts` starts the server with `onUnhandledRequest: "error"` — an unmocked request must fail
loudly rather than hit the network — resets handlers after each test, and calls
`resetFixtureState()` before each.

- [ ] **Step 5: Add the browser worker, opt-in only**

`test/msw/browser.ts` exports `worker`. `src/main.tsx` starts it only when
`import.meta.env.VITE_USE_MSW === "1"`, with a dynamic `import()` so the handlers are not in the
production bundle. Run `npx msw init public --save`.

- [ ] **Step 6: Run the suite and typecheck, then commit**

```bash
git add clients/mini-app
git commit -m "feat(mini-app): MSW harness with fixtures typed from the generated contract

Fixtures are typed as generated response types rather than structural literals,
so a backend rename breaks them at compile time. The sets handler honours the
ClientSetId idempotency rule -- replaying an id returns the original set
without appending -- which is what makes the re-tap test in a later task
actually prove something.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: The Telegram runtime, the fake, and the chrome hooks

**Files:**
- Create: `clients/mini-app/src/telegram/runtime.ts`, `fake-runtime.ts`, `TelegramProvider.tsx`,
  `useBackButton.ts`, `useMainButton.ts`, `useThemeParams.ts`
- Test: `clients/mini-app/src/telegram/chrome.test.tsx`, `fake-runtime.test.ts`

**Interfaces:**
- Produces:
  ```ts
  export interface TelegramButton {
    show(): void; hide(): void;
    setText(text: string): void;
    showProgress(): void; hideProgress(): void;
    onClick(cb: () => void): () => void;   // returns an unsubscribe function
  }
  export interface TelegramRuntime {
    initData: string;
    languageCode: string | null;
    themeParams: Record<string, string>;
    backButton: TelegramButton;
    mainButton: TelegramButton;
    expand(): void;
    enableClosingConfirmation(): void;
    disableClosingConfirmation(): void;
    notifySuccess(): void;
  }
  export function createRealRuntime(): TelegramRuntime;
  export function createFakeRuntime(opts?: { initData?: string }): TelegramRuntime;
  export function useTelegram(): TelegramRuntime;          // from TelegramProvider
  export function useBackButton(): void;
  export function useMainButton(opts: { text: string; onClick: () => void; visible?: boolean; loading?: boolean }): void;
  export function useThemeParams(): void;
  ```
  `onClick` returning its own unsubscribe function is deliberate: it makes the cleanup impossible to
  forget at the call site, which is the platform's canonical bug.

- [ ] **Step 1: Write the failing chrome tests**

These are the tests that stop a stale handler surviving an unmount.

```tsx
// clients/mini-app/src/telegram/chrome.test.tsx
import { expect, test, vi } from "vitest";
import { render } from "@testing-library/react";
import { TelegramProvider } from "./TelegramProvider";
import { useMainButton } from "./useMainButton";
import { createFakeRuntime } from "./fake-runtime";

function Screen({ label, onClick }: { label: string; onClick: () => void }) {
  useMainButton({ text: label, onClick, visible: true });
  return null;
}

test("the main button's click reaches only the currently mounted screen", () => {
  const runtime = createFakeRuntime();
  const first = vi.fn();
  const second = vi.fn();

  const view = render(
    <TelegramProvider runtime={runtime}><Screen label="Start" onClick={first} /></TelegramProvider>);
  view.rerender(
    <TelegramProvider runtime={runtime}><Screen label="Finish" onClick={second} /></TelegramProvider>);

  runtime.mainButton.click();           // fake-only helper: fires registered handlers

  expect(second).toHaveBeenCalledTimes(1);
  expect(first).not.toHaveBeenCalled();
});

test("unmounting removes the handler so a later click fires nothing", () => {
  const runtime = createFakeRuntime();
  const onClick = vi.fn();
  const view = render(
    <TelegramProvider runtime={runtime}><Screen label="Start" onClick={onClick} /></TelegramProvider>);
  view.unmount();

  runtime.mainButton.click();
  expect(onClick).not.toHaveBeenCalled();
});

test("the button text follows the mounted screen", () => {
  const runtime = createFakeRuntime();
  render(<TelegramProvider runtime={runtime}><Screen label="Finish workout" onClick={() => {}} /></TelegramProvider>);
  expect(runtime.mainButton.text).toBe("Finish workout");
});
```

The fake's buttons therefore expose `click()` and `text` beyond the `TelegramButton` interface. Type
the fake's return as `TelegramRuntime & { mainButton: FakeButton; backButton: FakeButton }`.

- [ ] **Step 2: Run to verify failure; implement the runtime, the fake, and the hooks**

`createRealRuntime()` delegates to `@telegram-apps/sdk`. `createFakeRuntime()` holds its own state
and renders nothing by itself — the on-page dev controls live in `TelegramProvider` and only when
the runtime reports `isFake`.

`useMainButton` must `setText`, `show`/`hide`, and subscribe in one effect whose cleanup
unsubscribes **and** hides the button; depend on `[text, visible, loading]` and keep `onClick` in a
ref so a changing callback identity does not resubscribe on every render.

- [ ] **Step 3: Write the fake's `initData` test**

The fake signs a real `initData` string with `VITE_DEV_BOT_TOKEN` so the backend's HMAC check passes
in development. The test asserts the string's **shape** — the sorted data-check-string and a 64-hex
`hash` parameter — not a fixed signature.

```ts
// clients/mini-app/src/telegram/fake-runtime.test.ts
import { expect, test } from "vitest";
import { createFakeRuntime } from "./fake-runtime";

test("the fake's initData is a signed, sorted query string the backend can validate", () => {
  const params = new URLSearchParams(createFakeRuntime().initData);
  expect(params.get("hash")).toMatch(/^[0-9a-f]{64}$/);
  expect(params.get("auth_date")).toMatch(/^\d+$/);
  expect(JSON.parse(params.get("user")!)).toHaveProperty("id");

  const keys = [...params.keys()].filter((k) => k !== "hash");
  expect(keys).toEqual([...keys].sort());
});
```

Signing uses `crypto.subtle` (HMAC-SHA256, key `"WebAppData"` over the bot token, then over the
data-check-string) — the same derivation as
`src/Shared/MyGym.Shared.Infrastructure/Auth/TelegramInitDataValidator.cs`. `initData` is therefore
produced asynchronously; expose `createFakeRuntime()` synchronously with an empty `initData` that is
populated by an awaited `ready()` the provider calls before rendering children, or make the factory
async — choose one and state it in the report.

- [ ] **Step 4: Run all tests, typecheck, commit**

```bash
git commit -m "feat(mini-app): Telegram runtime abstraction, dev fake, and chrome hooks

Telegram's Back and Main buttons are single global objects, so a handler left
bound after unmount fires a stale action -- this platform's canonical bug. The
TelegramButton interface therefore returns an unsubscribe function from
onClick, making cleanup impossible to forget, and two tests assert that a
click reaches only the mounted screen.

The fake signs a real initData with a development bot token, so the
HMAC-checked auth path runs end to end in a normal browser. Faking the
platform, not bypassing authentication.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Authentication

**Files:**
- Create: `clients/mini-app/src/auth/AuthProvider.tsx`, `clients/mini-app/src/auth/tokenStore.ts`
- Test: `clients/mini-app/src/auth/AuthProvider.test.tsx`

**Interfaces:**
- Consumes: `createApiClient` (Task 2), `useTelegram` (Task 4), MSW handlers (Task 3).
- Produces:
  - `tokenStore`: `{ get(): string | null; set(t: Tokens): void; clear(): void }` — module-scoped,
    in memory, no persistence.
  - `<AuthProvider>` — exchanges `initData` on mount, renders a spinner until signed in, an
    `ErrorBanner` with *Try again* if the exchange fails.
  - `useApi(): ApiClient` — the configured client, with `onUnauthorized` wired to refresh-then-
    re-exchange.

- [ ] **Step 1: Write the failing tests**

```tsx
// clients/mini-app/src/auth/AuthProvider.test.tsx
// Assertions (full bodies written during implementation against the handlers from Task 3):
// 1. "exchanges initData on mount and renders children once signed in"
// 2. "does not write any token to localStorage or sessionStorage"
//      -> after sign-in, expect(localStorage.length).toBe(0) and sessionStorage.length === 0
// 3. "refreshes once on a 401 and replays the original request"
// 4. "re-exchanges initData when the refresh is rejected with auth.refresh_token_invalid"
// 5. "renders an error with a retry action when the initial exchange fails with
//     auth.init_data_signature_invalid, and never renders children"
// 6. "two concurrent 401s trigger exactly one refresh"
//      -> fire two requests whose responses are both 401; assert the refresh handler saw one call
```

Assertion 2 is the one that enforces the design decision; assertion 6 is the one that prevents a
refresh stampede, and requires the provider to memoise the in-flight refresh promise.

- [ ] **Step 2: Run to verify failure; implement**

`onUnauthorized` holds a module-scoped `inFlightRefresh: Promise<boolean> | null`; concurrent callers
await the same promise and it is cleared in a `finally`.

- [ ] **Step 3: Run, typecheck, commit**

---

### Task 6: The query layer

**Files:**
- Create: `clients/mini-app/src/api/queryKeys.ts`, `clients/mini-app/src/api/queries.ts`,
  `clients/mini-app/src/api/queryClient.ts`
- Create: `clients/mini-app/src/catalog/useExerciseNames.ts`
- Test: `clients/mini-app/src/api/queries.test.tsx`, `clients/mini-app/src/catalog/useExerciseNames.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  export const qk = {
    profile: () => ["profile"] as const,
    exercises: (lang: string) => ["exercises", lang] as const,
    programs: () => ["programs"] as const,
    program: (id: string) => ["program", id] as const,
    dayPreview: (programId: string, dayId: string) => ["dayPreview", programId, dayId] as const,
    currentSession: () => ["session", "current"] as const,
    session: (id: string) => ["session", id] as const,
    sessions: (page: number) => ["sessions", page] as const,
  };
  export function useProfile(): UseQueryResult<ProfileResponse>;
  export function useProgramSummaries(): UseQueryResult<ProgramSummaryResponse[]>;
  export function useProgram(id: string): UseQueryResult<ProgramResponse>;
  export function useDayPreview(programId: string, dayId: string): UseQueryResult<DayPreviewResponse>;
  export function useCurrentSession(): UseQueryResult<SessionResponse | null>;
  export function useSession(id: string): UseQueryResult<SessionResponse>;
  export function useSessionSummaries(page: number): UseQueryResult<SessionSummaryResponse[]>;
  export function useExerciseNames(): { nameFor(id: string): string; isLoading: boolean };
  export function createQueryClient(): QueryClient;
  ```
  `qk.session(id)` is the key the optimistic mutation in Task 8 patches. `qk.currentSession()` is
  separate because 204 makes it a distinct resource, not the same session under another key.

- [ ] **Step 1: Write the failing tests**

```tsx
// Assertions:
// 1. "useCurrentSession resolves to null when the API answers 204"            <- the 204 contract
// 2. "useSession retries a 500 and resolves when the retry succeeds"
// 3. "useSession does not retry a 404"                                        <- 4xx must not retry
// 4. "retry honours Retry-After on a 429"
// 5. "useExerciseNames returns a readable placeholder for an id missing from the catalog"
//      -> expect(nameFor("00000000-0000-0000-0000-000000000000")).toBe("Unknown exercise")
// 6. "the exercise catalog is fetched once across two components in the same client"
```

Assertion 3 matters because the default TanStack retry count is 3 and would retry a 404 three times
before giving up, which is three pointless requests on every dead link.

- [ ] **Step 2: Implement `createQueryClient` with the spec's retry policy**

```ts
retry: (failureCount, error) => {
  if (!(error instanceof ApiError)) return failureCount < 3;   // network error
  if (error.status === 429) return failureCount < 3;
  if (error.status >= 500) return failureCount < 3;
  return false;                                                 // every other 4xx
},
retryDelay: (attempt, error) => {
  const retryAfter = error instanceof ApiError ? error.retryAfterMs : null;
  return retryAfter ?? Math.min(500 * 2 ** attempt, 8000);
},
```

`ApiError` gains a `retryAfterMs: number | null` parsed from the `Retry-After` header; add it in
this task and extend Task 2's tests rather than reaching back into that task's files silently.

`staleTime`: 30 s for sessions and programs, `Infinity` for the exercise catalog (seeded and
effectively immutable), 5 min for the profile. `refetchOnWindowFocus: true`,
`refetchOnReconnect: true`.

- [ ] **Step 3: Run, typecheck, commit**

---

### Task 7: Row derivation

**Files:**
- Create: `clients/mini-app/src/session/setRows.ts` (pure), `clients/mini-app/src/session/useSetRows.ts` (hook)
- Test: `clients/mini-app/src/session/setRows.test.ts`

This is the screen's model. It is a pure function so it can be tested exhaustively without React.

**Interfaces:**
- Produces:
  ```ts
  export type SetKind = "Warmup" | "Working";
  export type RowStatus = "pending" | "inFlight" | "logged" | "failed";
  export interface SetRow {
    rowKey: string;            // `${sessionExerciseId}:${kind}:${ordinal}`
    kind: SetKind;
    ordinal: number;           // 0-based within its kind
    weight: number;            // prescribed, or actual once logged
    reps: number;
    rir: number | null;
    status: RowStatus;
    isOptional: boolean;       // warm-up steps only
    loggedSetId: string | null;
    clientSetId: string | null;
    isExtra: boolean;          // logged beyond the prescribed count
  }
  export function deriveSetRows(exercise: SessionExerciseResponse, opts: { unit: string }): SetRow[];
  ```

- [ ] **Step 1: Write the failing tests**

```ts
// clients/mini-app/src/session/setRows.test.ts
import { describe, expect, test } from "vitest";
import { deriveSetRows } from "./setRows";
import type { components } from "../api/generated";

type Exercise = components["schemas"]["SessionExerciseResponse"];

function exercise(over: Partial<Exercise> = {}): Exercise {
  return {
    id: "se1", order: 0, exerciseId: "ex1", alternativeExerciseId: null,
    workingSetCount: 3, targetRepMin: 8, targetRepMax: 10,
    restMinSeconds: 120, restMaxSeconds: 180,
    suggestedWarmup: [
      { weight: 20, reps: 10, isOptional: false },
      { weight: 40, reps: 5, isOptional: false },
      { weight: 62.5, reps: 3, isOptional: true },
    ],
    suggestedWorkingWeight: 80,
    sets: [],
    ...over,
  } as Exercise;
}

describe("deriveSetRows", () => {
  test("produces one row per warm-up step then one per prescribed working set", () => {
    const rows = deriveSetRows(exercise(), { unit: "Kilograms" });
    expect(rows.map((r) => r.kind)).toEqual(
      ["Warmup", "Warmup", "Warmup", "Working", "Working", "Working"]);
    expect(rows.filter((r) => r.kind === "Warmup").map((r) => r.weight)).toEqual([20, 40, 62.5]);
  });

  test("pre-fills working rows at the suggested weight and the bottom of the rep range", () => {
    const rows = deriveSetRows(exercise(), { unit: "Kilograms" });
    const working = rows.filter((r) => r.kind === "Working");
    expect(working.every((r) => r.weight === 80 && r.reps === 8)).toBe(true);
  });

  test("a logged set replaces its row and shows the ACTUAL numbers, not the prescribed ones", () => {
    const rows = deriveSetRows(exercise({
      sets: [{ id: "s1", order: 0, kind: "Working", weight: 82.5, unit: "Kilograms", reps: 6, rir: 1,
               clientSetId: "c1", loggedAt: "2026-11-23T10:00:00Z" }],
    }), { unit: "Kilograms" });
    const first = rows.find((r) => r.kind === "Working")!;
    expect(first.status).toBe("logged");
    expect(first.weight).toBe(82.5);
    expect(first.reps).toBe(6);
    expect(first.rir).toBe(1);
    expect(first.loggedSetId).toBe("s1");
  });

  test("warm-up and working sets are matched within their own kind, not across kinds", () => {
    const rows = deriveSetRows(exercise({
      sets: [{ id: "w1", order: 0, kind: "Warmup", weight: 20, unit: "Kilograms", reps: 10, rir: null,
               clientSetId: "c1", loggedAt: "" }],
    }), { unit: "Kilograms" });
    expect(rows.filter((r) => r.status === "logged")).toHaveLength(1);
    expect(rows.find((r) => r.status === "logged")!.kind).toBe("Warmup");
    expect(rows.filter((r) => r.kind === "Working").every((r) => r.status === "pending")).toBe(true);
  });

  test("sets logged beyond the prescribed count appear as extra rows after the prescribed ones", () => {
    const sets = [0, 1, 2, 3].map((i) => ({
      id: `s${i}`, order: i, kind: "Working" as const, weight: 80, unit: "Kilograms",
      reps: 8, rir: null, clientSetId: `c${i}`, loggedAt: "",
    }));
    const rows = deriveSetRows(exercise({ sets }), { unit: "Kilograms" });
    const working = rows.filter((r) => r.kind === "Working");
    expect(working).toHaveLength(4);
    expect(working[3]!.isExtra).toBe(true);
    expect(working.slice(0, 3).every((r) => !r.isExtra)).toBe(true);
  });

  test("row keys are stable and unique, because they key the idempotency map", () => {
    const rows = deriveSetRows(exercise(), { unit: "Kilograms" });
    expect(new Set(rows.map((r) => r.rowKey)).size).toBe(rows.length);
    expect(rows[3]!.rowKey).toBe("se1:Working:0");
    expect(deriveSetRows(exercise(), { unit: "Kilograms" })[3]!.rowKey).toBe(rows[3]!.rowKey);
  });

  test("carries isOptional from the warm-up step", () => {
    const rows = deriveSetRows(exercise(), { unit: "Kilograms" });
    expect(rows.map((r) => r.isOptional).slice(0, 3)).toEqual([false, false, true]);
  });

  test("a null suggested working weight yields rows at 0 rather than NaN or a crash", () => {
    const rows = deriveSetRows(exercise({ suggestedWorkingWeight: null }), { unit: "Kilograms" });
    expect(rows.filter((r) => r.kind === "Working").every((r) => r.weight === 0)).toBe(true);
  });

  test("a set logged in the other unit keeps its own unit and is not silently converted", () => {
    const rows = deriveSetRows(exercise({
      sets: [{ id: "s1", order: 0, kind: "Working", weight: 180, unit: "Pounds", reps: 8, rir: null,
               clientSetId: "c1", loggedAt: "" }],
    }), { unit: "Kilograms" });
    const logged = rows.find((r) => r.status === "logged")!;
    expect(logged.weight).toBe(180);
    // The row records the unit it was logged in so the UI can label it; no arithmetic is attempted.
  });
});
```

The last test encodes a real decision: the client never converts units. `LoggedSetResponse.unit` is
displayed as logged. Add a `unit: string` field to `SetRow` for this.

- [ ] **Step 2: Run to verify failure; implement `deriveSetRows`**

Group `exercise.sets` by `kind`, sort each group by `order`, then zip against the prescribed counts.
Extra sets become rows with `isExtra: true`. `status` is `"logged"` for matched rows and `"pending"`
otherwise; `"inFlight"` and `"failed"` are applied by Task 8 from the mutation state, not here.

- [ ] **Step 3: Run, typecheck, commit**

---

### Task 8: The idempotency map and the optimistic set-log mutation

**Files:**
- Create: `clients/mini-app/src/session/usePendingSetIds.tsx`,
  `clients/mini-app/src/api/mutations.ts`
- Test: `clients/mini-app/src/session/usePendingSetIds.test.tsx`,
  `clients/mini-app/src/api/logSet.test.tsx`

**This is the task the whole app exists to get right.** A defect here silently duplicates or loses a
logged set.

**Interfaces:**
- Produces:
  ```ts
  export function usePendingSetIds(): {
    idFor(rowKey: string): string;     // assigns on first call, returns the same id thereafter
    release(rowKey: string): void;     // called only once the set is confirmed logged
  };
  export function useLogSet(sessionId: string): UseMutationResult<LoggedSetResponse, ApiError, LogSetVars>;
  export interface LogSetVars {
    sessionExerciseId: string; rowKey: string;
    kind: SetKind; weight: number; reps: number; rir: number | null;
  }
  export function useCompleteSession(sessionId: string): UseMutationResult<void, ApiError, void>;
  export function useAbandonSession(sessionId: string): UseMutationResult<void, ApiError, void>;
  export function useStartSession(): UseMutationResult<SessionResponse, ApiError, { programId?: string; programDayId?: string; bodyweight?: number }>;
  ```

- [ ] **Step 1: Write the failing tests for the id map**

```tsx
// Assertions:
// 1. "returns the same id for the same rowKey across calls"
// 2. "returns different ids for different rowKeys"
// 3. "keeps the id after a consumer component unmounts and remounts"
//      -> render a child that calls idFor, unmount it, remount it, assert the id is identical.
//         This is why the map lives in the provider, not in component state.
// 4. "release() frees the key so a later row at the same position gets a fresh id"
// 5. "ids are RFC-4122 shaped"
```

Test 3 is the one that proves the map belongs above the component tree.

- [ ] **Step 2: Implement with a `useRef<Map<string, string>>` inside a provider**

Use `crypto.randomUUID()`. The map must **not** be React state — assigning an id during an event
handler must not trigger a re-render.

- [ ] **Step 3: Write the failing tests for the optimistic mutation**

```tsx
// clients/mini-app/src/api/logSet.test.tsx
// Assertions, each named for the behaviour it protects:
//
// 1. "the row appears as logged before the server responds"
//      Delay the MSW handler; assert the optimistic set is in the session cache immediately.
//
// 2. "the optimistic entry is replaced by the server's set, matched on clientSetId"
//      Assert the final cache entry has the server's id and exactly one set, not two.
//
// 3. "a re-tap after a failed write sends the SAME clientSetId"           <- THE critical test
//      First POST fails with a network error; re-tap; assert both recorded requests carry an
//      identical clientSetId. Assert against the captured request bodies, not internal state.
//
// 4. "a failed write stays on screen as failed and is not rolled back"
//      After retries are exhausted, the set is still in the cache with status "failed".
//
// 5. "session.closed drops the optimistic set and refetches"               <- the one exception
//      Assert the optimistic entry is gone and the session query was invalidated.
//
// 6. "a 409 is not retried"
//      Assert the handler was called exactly once.
//
// 7. "logging does not refetch the whole session"
//      Assert GET /api/sessions/:id was not called between the optimistic patch and the response.
//
// 8. "a background refetch during an in-flight write does not duplicate the set"
//      Trigger a refetch mid-flight; assert the final cache has one set with that clientSetId.
```

Test 3 is non-negotiable: it is the test that stops duplicated sets. Capture request bodies with an
MSW `server.events.on("request:start", ...)` listener or a handler that pushes to an array.

- [ ] **Step 4: Run to verify failure; implement `useLogSet`**

`onMutate`: read `idFor(vars.rowKey)`, `cancelQueries` on `qk.session(sessionId)`, snapshot the
previous data, and `setQueryData` appending a `LoggedSetResponse`-shaped entry with the
`clientSetId`, a synthetic `id` of `""`, and a local marker. Return the snapshot as context.

`onSuccess`: `setQueryData`, replacing the entry whose `clientSetId` matches with the server's set;
then `release(vars.rowKey)`.

`onError`: if `error.errorCode === "session.closed"`, restore the snapshot and
`invalidateQueries(qk.session(sessionId))`. Otherwise mark the matching entry failed — **do not**
restore the snapshot, because that would remove the athlete's set.

The local status marker must not collide with the generated type. Keep it out of band: a
`Map<string, RowStatus>` in the same provider as the id map, keyed by `clientSetId`, rather than a
non-contract field smuggled onto `LoggedSetResponse`.

- [ ] **Step 5: Run every test in the file, typecheck, commit**

```bash
git commit -m "feat(mini-app): optimistic set logging keyed to the backend's idempotency index

The backend holds a unique index on ClientSetId, so replaying a set-log
returns the original rather than duplicating. The client's duty is to reuse the
same id for the same logical set, and the dangerous case is a manual re-tap
after a lost response -- automatic retries replay the same variables, but a
fresh tap would mint a new id and create a second set. The id is therefore
keyed to the row, held above the component tree so it survives a remount, and
released only once the set is confirmed.

A failed write is never rolled back; the athlete performed the set and has the
number in their head for about thirty seconds. The single exception is
session.closed, where retrying is impossible rather than merely failed.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: The rest timer

**Files:**
- Create: `clients/mini-app/src/session/RestTimerProvider.tsx`, `clients/mini-app/src/session/RestTimer.tsx`
- Test: `clients/mini-app/src/session/restTimer.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  export function useRestTimer(): {
    start(seconds: number, label: string): void;
    dismiss(): void;
    remainingMs: number | null;   // null when not running
    label: string | null;
  };
  ```

- [ ] **Step 1: Write the failing tests**

```tsx
// Assertions (use vi.useFakeTimers and vi.setSystemTime):
// 1. "counts down from the given duration"
// 2. "is correct after a backgrounded gap, because it stores a target timestamp"
//      start(180); advance the SYSTEM CLOCK by 170s without running timers (simulating a
//      suspended webview); assert remainingMs is ~10s, not ~180s.
//      This is the assertion that rules out a decrementing counter.
// 3. "fires the success haptic exactly once at zero"
// 4. "does not go negative"
// 5. "dismiss() clears it immediately"
// 6. "starting a new timer replaces the running one rather than stacking"
// 7. "survives a route change"  -> render inside a router, navigate, assert still running
```

- [ ] **Step 2: Run to verify failure; implement**

State is `{ targetAt: number; label: string } | null`. A single `setInterval(..., 250)` recomputes
`targetAt - Date.now()` and clears itself at zero after calling `notifySuccess()` once, guarded by a
ref so a re-render cannot double-fire it. The provider sits **above** the router in `App.tsx`.

- [ ] **Step 3: Run, typecheck, commit**

---

### Task 10: The session screen

**Files:**
- Create: `clients/mini-app/src/session/SessionScreen.tsx`, `ExerciseCard.tsx`, `SetRow.tsx`
- Create: `clients/mini-app/src/ui/Stepper.tsx`, `Button.tsx`, `Spinner.tsx`, `ErrorBanner.tsx`
- Create: `clients/mini-app/src/session/session.css`
- Test: `clients/mini-app/src/session/SessionScreen.test.tsx`

**Interfaces:**
- Consumes: `useSession`, `deriveSetRows`, `useLogSet`, `usePendingSetIds`, `useRestTimer`,
  `useExerciseNames`, `useMainButton`, `useTelegram`.
- Produces: `<SessionScreen sessionId={string} />`, which reads `status` from the session and renders
  read-only when it is not `"InProgress"`.

- [ ] **Step 1: Write the failing component tests**

```tsx
// Assertions:
// 1. "renders every exercise with its name from the catalog and its target"
// 2. "pre-fills the first pending working row at the suggested weight and TargetRepMin"
//      -> the row shows 80 and 8 before any interaction
// 3. "renders warm-up weights 20, 40 and 62.5 for an 80 kg working weight"
//      the backend's round-DOWN values; a round-to-nearest implementation would show 22.5/42.5/65
// 4. "one tap on Log confirms the row and starts the rest timer"
// 5. "tapping the weight opens a stepper that steps by the profile's loading increment"
//      -> two taps on + moves 80 to 85 with a 2.5 increment
// 6. "the expanded card is the first with unlogged prescribed sets"
// 7. "when every prescribed set is logged, the LAST card stays expanded"
// 8. "+ add set appends a pending working row beyond the prescribed count"
// 9. "the Telegram main button reads Finish workout and completing navigates away"
// 10. "a completed session renders read-only: no Log buttons, no steppers, no add set"
// 11. "enableClosingConfirmation is called for a live session and disabled on unmount"
// 12. "a failed write shows the row as unsynced with a Retry affordance"
```

Assertion 3 is a regression guard on the warm-up rounding direction — the defect that required an
amendment to the backend spec. Assertion 11 is the swipe-down data-loss guard.

- [ ] **Step 2: Run to verify failure; implement the three components**

`SessionScreen` resolves the expanded card, renders the header (day label, elapsed time,
`loggedWorkingSets / prescribedWorkingSets`), the card list, and the sticky `RestTimer`. It owns the
Main button: *Finish workout* when live, nothing when read-only.

`ExerciseCard` renders the name, the target line (`3 × 8-10 · rest 2-3m`), and its rows grouped
under `warm-up` and `working` headings.

`SetRow` renders by `status`: `pending` shows weight × reps and a **Log** button; tapping a number
swaps that number for a `Stepper`; `inFlight` disables the button and shows a spinner; `logged`
renders a compact confirmed line with the actual numbers and its unit; `failed` renders the numbers
with an unsynced marker and **Retry**.

Styling is plain CSS using the custom properties `useThemeParams` sets. No CSS framework.

- [ ] **Step 3: Run, typecheck, commit**

---

### Task 11: Today and Day preview

**Files:**
- Create: `clients/mini-app/src/today/TodayScreen.tsx`, `clients/mini-app/src/today/todaysDay.ts`
- Create: `clients/mini-app/src/preview/DayPreviewScreen.tsx`
- Test: `clients/mini-app/src/today/todaysDay.test.ts`, `TodayScreen.test.tsx`,
  `clients/mini-app/src/preview/DayPreviewScreen.test.tsx`

**Interfaces:**
- Produces:
  - `function pickTodaysDay(days: ProgramDayResponse[], weekday: number): { today: ProgramDayResponse | null; rest: ProgramDayResponse[] }`
  - `<TodayScreen />`, `<DayPreviewScreen />`

- [ ] **Step 1: Write the failing tests for `pickTodaysDay`**

```ts
// Assertions:
// 1. "promotes the day whose dayOfWeek matches and keeps the rest in Order"
// 2. "promotes nothing when no day matches, and returns all days in Order"
// 3. "promotes nothing when every dayOfWeek is null (an unscheduled program)"
// 4. "promotes only the first when two days share a weekday"
// 5. "maps the generated DayOfWeek string union to the JS 0=Sunday weekday correctly"
//      dayOfWeek is "Sunday" | ... | "Saturday" | null, so the mapping must be an explicit
//      table, not a cast or an index. Assert every one of the seven, because getting
//      Sunday=0 versus Monday=0 wrong shows the athlete the wrong workout.
```

Assertion 5 is a real trap: .NET's `DayOfWeek` is `Sunday = 0`, which matches JavaScript's
`Date.getDay()`, but if OpenAPI emitted a string union the mapping must be explicit.

- [ ] **Step 2: Implement, then write the screen tests**

```tsx
// TodayScreen assertions:
// 1. "lists the active program's days with today's labelled Today"
// 2. "leads with Resume workout when GET /api/sessions/current returns a session"
// 3. "does not show Resume when /current answers 204"
// 4. "shows an empty state with guidance when the athlete has no active program"
// 5. "tapping a day navigates to its preview"

// DayPreviewScreen assertions:
// 1. "shows each exercise with its name, target reps, rest, and resolved warm-up weights"
// 2. "the Telegram main button starts the session and navigates to it"
// 3. "a 409 session.already_in_progress navigates to the open session instead of showing an error"
// 4. "shows the suggested working weight, or a dash when it is null (never 'null' or NaN)"
```

Preview assertion 3 implements the `resumeCurrentSession` recovery action from Task 2.

- [ ] **Step 3: Run, typecheck, commit**

---

### Task 12: History, read-only sessions, and Profile

**Files:**
- Create: `clients/mini-app/src/history/HistoryScreen.tsx`, `clients/mini-app/src/profile/ProfileScreen.tsx`
- Test: `HistoryScreen.test.tsx`, `ProfileScreen.test.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
// HistoryScreen assertions:
// 1. "lists past sessions newest first with their date and working-set count"
// 2. "tapping a session opens it read-only"
// 3. "loads the next page and appends"
// 4. "shows an empty state before the first workout"

// ProfileScreen assertions:
// 1. "shows the current unit, loading increment and bodyweight"
// 2. "saving sends a full PATCH /api/me -- every field, since PATCH is a full replace"
// 3. "a validation error (profile.time_zone_unknown) is shown against the field, not as a toast"
// 4. "changing the unit updates the weights shown on a session screen"
```

Profile assertion 2 encodes a documented backend quirk: `PATCH /api/me` is a **full replace**
(`ProfileEndpoints.cs` says so), so omitting a field clears it. The client must send everything.

- [ ] **Step 2: Implement, run, typecheck, commit**

---

### Task 13: Router, chrome wiring, error boundary, build and CI

**Files:**
- Create: `clients/mini-app/src/routes.tsx`, `clients/mini-app/src/ErrorBoundary.tsx`
- Modify: `clients/mini-app/src/App.tsx`, `src/main.tsx`
- Create: `.github/workflows/mini-app.yml` (or extend the existing workflow if one is present —
  check `.github/workflows/` first)
- Create: `clients/mini-app/README.md`
- Test: `clients/mini-app/src/App.test.tsx`

- [ ] **Step 1: Write the failing integration-ish tests**

```tsx
// Assertions:
// 1. "the five routes render their screens"
// 2. "the Telegram back button is hidden at the root and shown on a child route"
// 3. "the back button navigates back"
// 4. "expand() is called once at launch"
// 5. "a screen that throws renders the error boundary with Reload, and the app is not blank"
// 6. "the full happy path: sign in, pick today, start, log a working set, finish"
//      One test through the whole app against MSW. The single most valuable test in the file.
```

- [ ] **Step 2: Implement the router and providers**

`App.tsx` nests, outermost first: `TelegramProvider` → `QueryClientProvider` → `AuthProvider` →
`RestTimerProvider` → `PendingSetIdsProvider` → `RouterProvider`. `RestTimerProvider` above the
router is what lets the timer survive navigation.

- [ ] **Step 3: Add CI**

Check for an existing workflow and extend it rather than adding a second. Steps: `npm ci`,
`npm run typecheck`, `npm test`, `npm run build`, then the regeneration check —
`bash scripts/dump-openapi.sh clients/mini-app/openapi.json && npm run api:types && git diff --exit-code clients/mini-app/openapi.json clients/mini-app/src/api/generated.ts`.

- [ ] **Step 4: Write `clients/mini-app/README.md`**

How to run against mocks (`VITE_USE_MSW=1 VITE_TELEGRAM_FAKE=1 npm run dev`), how to run against a
local API, how to regenerate types, and the explicit statement that real-device verification over a
tunnel is still outstanding.

- [ ] **Step 5: Run the whole suite, typecheck, build, commit**

---

## Self-Review

**Spec coverage.** §1 scope items 1–8 map to Tasks 5, 11, 11, 8+10, 9, 10, 12, 12. §2 stack → Task 1.
§3 generated types → Task 1. §4 auth → Task 5. §5 screens and chrome → Tasks 4, 11, 12, 13. §6
session screen → Tasks 7, 10. §7 optimistic writes → Task 8. §8 timer and catalog → Tasks 9, 6. §9
dev harness → Tasks 3, 4. §10 error handling → Tasks 2, 13. §11 testing → every task. §12 build and
deploy → Task 13. No spec section is unimplemented.

**Known gaps, accepted deliberately.** The spec's §12 Caddy configuration is not a task: there is no
Caddyfile in this repository yet, and inventing one ahead of a deployment target would be guesswork.
Recorded here rather than silently dropped.

**Type consistency.** `SetRow` is defined once in Task 7 and consumed in Tasks 8 and 10. `qk.*` keys
are defined in Task 6 and used in Tasks 8, 10, 11, 12. `TelegramRuntime` is defined in Task 4 and
consumed in Tasks 10, 11, 13. `ApiError` is defined in Task 2 and extended with `retryAfterMs` in
Task 6 — **flagged explicitly** as the one place a later task reaches back into an earlier task's
file, with instructions to extend that task's tests rather than edit silently.

**Casing and shapes: resolved, not assumed.** The document was generated and inspected before this
plan was finalised, so the code samples are written against what is actually emitted rather than
against a guess:

- Property names are **camelCase** (`workingSetCount`, `clientSetId`, `suggestedWorkingWeight`).
- Enums are **string unions**: `dayOfWeek` is `"Sunday" | … | "Saturday" | null`, `unit` is
  `"Kilograms" | "Pounds"`, `kind` is `"Warmup" | "Working"`, `status` on a session is a string.
- Numbers are plain `number`, with nullables as `number | null` — see `366fcce`, which narrowed the
  `number | string` unions .NET emits for every `int` and `decimal`.
- `PreviewExerciseResponse` carries `weightUnit`; `SessionExerciseResponse` does **not**. Task 10
  needs the profile for its unit label; Task 11's preview does not.

Task 1 Step 6 still verifies all of this, because the contract can change under the plan; it is a
regression guard rather than a discovery step.
