import { beforeEach, describe, expect, test } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import type { ReactNode } from "react";
import { server } from "../../test/msw/server";
import { IDS } from "../../test/msw/fixtures";
import { TelegramProvider } from "../telegram/TelegramProvider";
import { createFakeRuntime } from "../telegram/fake-runtime";
import { AuthProvider } from "../auth/AuthProvider";
import { clearTokens } from "../auth/tokenStore";
import { PendingSetIdsProvider, usePendingSetIds } from "../session/PendingSetIdsProvider";
import { createQueryClient } from "./queryClient";
import { useSession } from "./queries";
import { useLogSet } from "./mutations";
import { qk } from "./queryKeys";
import type { components } from "./generated";

type Session = components["schemas"]["SessionResponse"];

/** Bodies of every POST to the sets endpoint, in order, so a re-tap can be inspected. */
let loggedBodies: { clientSetId: string; weight: number; reps: number }[] = [];
/** GET /api/sessions/:id calls, to prove a write does not refetch the session. */
let sessionFetches = 0;

beforeEach(() => {
  clearTokens();
  loggedBodies = [];
  sessionFetches = 0;
});

function recordRequests(): void {
  server.events.removeAllListeners();
  server.events.on("request:start", ({ request }) => {
    if (request.method === "GET" && /\/api\/sessions\/[0-9a-f-]+$/.test(new URL(request.url).pathname)) {
      sessionFetches += 1;
    }
  });
}

let queryClient: QueryClient;

function harness(children: ReactNode) {
  queryClient = createQueryClient();
  queryClient.setDefaultOptions({
    queries: { ...queryClient.getDefaultOptions().queries, retryDelay: 0 },
    mutations: { ...queryClient.getDefaultOptions().mutations, retryDelay: 0 },
  });

  return (
    <TelegramProvider runtime={createFakeRuntime({ initData: "signed" })}>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <PendingSetIdsProvider>{children}</PendingSetIdsProvider>
        </AuthProvider>
      </QueryClientProvider>
    </TelegramProvider>
  );
}

/**
 * A minimal stand-in for one set row: a Log button, the cache's view of this exercise's sets, and
 * the row's local status.
 */
function Row({ sessionId, onLogged }: { sessionId: string; onLogged?: () => void }) {
  const session = useSession(sessionId);
  const { peek, statuses } = usePendingSetIds();
  const rowKey = `${IDS.benchSessionExerciseId}:Working:0`;
  const log = useLogSet(sessionId, onLogged === undefined ? undefined : { onLogged: () => onLogged() });

  if (!session.isSuccess) return <div>loading</div>;

  const bench = session.data.exercises.find((e) => e.id === IDS.benchSessionExerciseId);
  const id = peek(rowKey);

  return (
    <div>
      <button
        type="button"
        onClick={() =>
          log.mutate({
            sessionExerciseId: IDS.benchSessionExerciseId,
            rowKey,
            kind: "Working",
            weight: 80,
            reps: 8,
            rir: 2,
            unit: "Kilograms",
          })
        }
      >
        Log
      </button>
      <output data-testid="count">{bench?.sets.length ?? 0}</output>
      <output data-testid="status">{id === null ? "none" : (statuses.get(id) ?? "none")}</output>
      <output data-testid="working">{session.data.workingSetCount}</output>
      <output data-testid="weights">{bench?.sets.map((s) => `${s.weight}x${s.reps}`).join(",") ?? ""}</output>
    </div>
  );
}

async function openSession(): Promise<string> {
  const response = await fetch("/api/sessions", {
    method: "POST",
    body: JSON.stringify({}),
    headers: { "content-type": "application/json" },
  });
  return ((await response.json()) as Session).id;
}

/** Re-dispatches to the real fixture handler so state still mutates while bodies are recorded. */
async function passthroughToFixtures(request: Request): Promise<Response> {
  const { logSet } = await import("../../test/msw/fixtures");
  const url = new URL(request.url);
  const exerciseId = url.pathname.split("/").at(-2) ?? "";
  const body = (await request.json()) as {
    clientSetId: string;
    kind: "Warmup" | "Working";
    weight: number;
    reps: number;
    rir: number | null;
  };
  const result = logSet(exerciseId, body);
  if (result === null) {
    return HttpResponse.json({ title: "no", status: 404, errorCode: "session.exercise_not_found" }, { status: 404 });
  }
  return HttpResponse.json(result.set, { status: 201 });
}

describe("optimistic set logging", () => {
  test("the row appears as logged before the server responds", async () => {
    const sessionId = await openSession();
    let release: (() => void) | undefined;
    server.use(
      http.post("/api/sessions/:id/exercises/:exerciseId/sets", async ({ request }) => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return passthroughToFixtures(request);
      }),
    );

    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });
    await userEvent.click(screen.getByRole("button", { name: "Log" }));

    // Visible while the request is still hanging: the athlete is already racking the bar.
    await waitFor(() => expect(screen.getByTestId("count")).toHaveTextContent("1"));
    expect(screen.getByTestId("status")).toHaveTextContent("inFlight");

    release?.();
    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("none"));
  });

  test("the optimistic entry is replaced by the server's set, matched on clientSetId", async () => {
    const sessionId = await openSession();
    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });

    await userEvent.click(screen.getByRole("button", { name: "Log" }));

    // One set, not two: matching on clientSetId is what stops the optimistic copy and the server's
    // copy coexisting.
    await waitFor(() => expect(screen.getByTestId("count")).toHaveTextContent("1"));

    const cached = queryClient.getQueryData<Session>(qk.session(sessionId));
    const sets = cached?.exercises.find((e) => e.id === IDS.benchSessionExerciseId)?.sets ?? [];
    expect(sets).toHaveLength(1);
    expect(sets[0]?.id).not.toBe("");
    expect(sets[0]?.id).toMatch(/^99999999-/);
  });

  test("a re-tap after a failed write sends the SAME clientSetId", async () => {
    // The test that stops duplicated sets. The first POST fails as if the response were lost in
    // transit; the athlete taps again. Both requests must carry one id, so the backend's unique
    // index recognises the second as a replay and returns the original set.
    const sessionId = await openSession();
    let attempts = 0;
    server.use(
      http.post("/api/sessions/:id/exercises/:exerciseId/sets", async ({ request }) => {
        attempts += 1;
        const body = (await request.clone().json()) as { clientSetId: string; weight: number; reps: number };
        loggedBodies.push(body);
        // Every automatic attempt fails, so the row actually reaches the failed state and the
        // athlete is the one who retries. Three retries means four requests, so the cutoff is 4;
        // at 3 the fourth automatic request succeeded and a manual re-tap was never exercised.
        if (attempts <= 4) return HttpResponse.error();
        return passthroughToFixtures(request);
      }),
    );

    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });

    await userEvent.click(screen.getByRole("button", { name: "Log" }));
    // Real backoff: 500 + 1000 + 2000 ms across the three automatic attempts. Left unstubbed so
    // this test exercises the retry policy the app actually ships.
    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("failed"), { timeout: 15_000 });

    await userEvent.click(screen.getByRole("button", { name: "Log" }));
    await waitFor(() => expect(screen.getByTestId("count")).toHaveTextContent("1"), { timeout: 15_000 });

    // Asserted against the recorded request bodies, not against internal state: three failed
    // automatic attempts plus the athlete's own tap, all carrying one id.
    // Four automatic requests (one plus three retries) and then the athlete's own tap.
    expect(loggedBodies.length).toBe(5);
    expect(new Set(loggedBodies.map((b) => b.clientSetId)).size).toBe(1);
  }, 30_000);

  test("a failed write stays on screen as failed and is not rolled back", async () => {
    const sessionId = await openSession();
    server.use(http.post("/api/sessions/:id/exercises/:exerciseId/sets", () => HttpResponse.error()));

    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });
    await userEvent.click(screen.getByRole("button", { name: "Log" }));

    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("failed"), { timeout: 15_000 });
    // Still there. Removing a set the athlete performed is worse than showing it unsynced.
    expect(screen.getByTestId("count")).toHaveTextContent("1");
    expect(screen.getByTestId("weights")).toHaveTextContent("80x8");
  }, 30_000);

  test("session.closed drops the optimistic set, because retrying could never succeed", async () => {
    const sessionId = await openSession();
    server.use(
      http.post("/api/sessions/:id/exercises/:exerciseId/sets", () =>
        HttpResponse.json(
          { title: "finished", status: 409, errorCode: "session.closed" },
          { status: 409 },
        ),
      ),
    );

    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });
    await userEvent.click(screen.getByRole("button", { name: "Log" }));

    // The one exception to "never roll back": the set can never be stored against a closed
    // session, so a Retry affordance would lie.
    await waitFor(() => expect(screen.getByTestId("count")).toHaveTextContent("0"));
  });

  test("a 409 is not retried", async () => {
    const sessionId = await openSession();
    let calls = 0;
    server.use(
      http.post("/api/sessions/:id/exercises/:exerciseId/sets", () => {
        calls += 1;
        return HttpResponse.json(
          { title: "finished", status: 409, errorCode: "session.closed" },
          { status: 409 },
        );
      }),
    );

    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });
    await userEvent.click(screen.getByRole("button", { name: "Log" }));

    await waitFor(() => expect(screen.getByTestId("count")).toHaveTextContent("0"));
    expect(calls).toBe(1);
  });

  test("logging does not refetch the whole session", async () => {
    const sessionId = await openSession();
    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });

    recordRequests();
    await userEvent.click(screen.getByRole("button", { name: "Log" }));
    await waitFor(() => expect(screen.getByTestId("count")).toHaveTextContent("1"));

    // A refetch racing the optimistic patch is how optimistic UIs flicker. The cache is patched
    // instead.
    expect(sessionFetches).toBe(0);
  });

  test("the optimistic patch increments the working-set count", async () => {
    const sessionId = await openSession();
    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });

    expect(screen.getByTestId("working")).toHaveTextContent("0");
    await userEvent.click(screen.getByRole("button", { name: "Log" }));

    await waitFor(() => expect(screen.getByTestId("working")).toHaveTextContent("1"));
  });

  test("the onLogged callback fires on the tap, not on the response", async () => {
    const sessionId = await openSession();
    let logged = 0;
    let release: (() => void) | undefined;
    server.use(
      http.post("/api/sessions/:id/exercises/:exerciseId/sets", async ({ request }) => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return passthroughToFixtures(request);
      }),
    );

    render(harness(<Row sessionId={sessionId} onLogged={() => (logged += 1)} />));
    await screen.findByRole("button", { name: "Log" });
    await userEvent.click(screen.getByRole("button", { name: "Log" }));

    // The rest timer hangs off this, and it must not wait for the network.
    await waitFor(() => expect(logged).toBe(1));
    release?.();
  });

  test("a second tap while the first is still in flight does not create two sets", async () => {
    // Genuinely concurrent: the response is held open across both taps. Once a set is confirmed
    // the real UI turns that row into a logged row with no Log button, so the only way to tap
    // twice is while the first write is still outstanding.
    const sessionId = await openSession();
    let release: (() => void) | undefined;
    server.use(
      http.post("/api/sessions/:id/exercises/:exerciseId/sets", async ({ request }) => {
        const body = (await request.clone().json()) as {
          clientSetId: string;
          weight: number;
          reps: number;
        };
        loggedBodies.push(body);
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return passthroughToFixtures(request);
      }),
    );

    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });

    await userEvent.click(screen.getByRole("button", { name: "Log" }));
    await userEvent.click(screen.getByRole("button", { name: "Log" }));
    release?.();

    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("none"));

    // Every request carried the row's one id, so the backend's unique index collapses them.
    expect(new Set(loggedBodies.map((b) => b.clientSetId)).size).toBe(1);
    const cached = queryClient.getQueryData<Session>(qk.session(sessionId));
    expect(cached?.exercises.find((e) => e.id === IDS.benchSessionExerciseId)?.sets).toHaveLength(1);
  });

  test("a background refetch during an in-flight write does not duplicate the set", async () => {
    const sessionId = await openSession();
    let release: (() => void) | undefined;
    server.use(
      http.post("/api/sessions/:id/exercises/:exerciseId/sets", async ({ request }) => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return passthroughToFixtures(request);
      }),
    );

    render(harness(<Row sessionId={sessionId} />));
    await screen.findByRole("button", { name: "Log" });
    await userEvent.click(screen.getByRole("button", { name: "Log" }));
    await waitFor(() => expect(screen.getByTestId("count")).toHaveTextContent("1"));

    // The webview waking up mid-write.
    await queryClient.refetchQueries({ queryKey: qk.session(sessionId) });
    release?.();

    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("none"));
    const cached = queryClient.getQueryData<Session>(qk.session(sessionId));
    const sets = cached?.exercises.find((e) => e.id === IDS.benchSessionExerciseId)?.sets ?? [];
    expect(sets).toHaveLength(1);
  });
});
