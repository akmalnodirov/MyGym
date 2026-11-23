import { describe, expect, test } from "vitest";
import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import type { ReactNode } from "react";
import { server } from "../../test/msw/server";
import { IDS } from "../../test/msw/fixtures";
import { TelegramProvider } from "../telegram/TelegramProvider";
import { createFakeRuntime } from "../telegram/fake-runtime";
import { AuthProvider } from "../auth/AuthProvider";
import { clearTokens } from "../auth/tokenStore";
import { ApiError } from "./ApiError";
import { createQueryClient, retryDelay, shouldRetry } from "./queryClient";
import { useCurrentSession, useExerciseNames, useProfile, useSession } from "./queries";

function harness(children: ReactNode) {
  clearTokens();
  const client = createQueryClient();
  // Zero delay so a retry test does not wait out real backoff.
  client.setDefaultOptions({ queries: { ...client.getDefaultOptions().queries, retryDelay: 0 } });

  return (
    <TelegramProvider runtime={createFakeRuntime({ initData: "signed" })}>
      <QueryClientProvider client={client}>
        <AuthProvider>{children}</AuthProvider>
      </QueryClientProvider>
    </TelegramProvider>
  );
}

describe("the retry policy", () => {
  test("retries a network failure, which is what a basement gym produces", () => {
    expect(shouldRetry(0, new TypeError("Failed to fetch"))).toBe(true);
    expect(shouldRetry(2, new TypeError("Failed to fetch"))).toBe(true);
  });

  test("gives up after three attempts", () => {
    expect(shouldRetry(3, new TypeError("Failed to fetch"))).toBe(false);
  });

  test("retries 5xx and 429 but no other 4xx", () => {
    const error = (status: number) =>
      new ApiError({ status, errorCode: null, title: "", retryAfterMs: null });

    expect(shouldRetry(0, error(500))).toBe(true);
    expect(shouldRetry(0, error(503))).toBe(true);
    expect(shouldRetry(0, error(429))).toBe(true);

    // Repeating a rejected payload cannot help, and a 404 retried three times is three pointless
    // requests for every dead link.
    expect(shouldRetry(0, error(404))).toBe(false);
    expect(shouldRetry(0, error(409))).toBe(false);
    expect(shouldRetry(0, error(422))).toBe(false);
    expect(shouldRetry(0, error(400))).toBe(false);
  });

  test("honours Retry-After over the backoff curve", () => {
    const throttled = new ApiError({ status: 429, errorCode: null, title: "", retryAfterMs: 4000 });

    expect(retryDelay(0, throttled)).toBe(4000);
  });

  test("backs off exponentially and caps", () => {
    expect(retryDelay(0, new Error("x"))).toBe(500);
    expect(retryDelay(1, new Error("x"))).toBe(1000);
    expect(retryDelay(2, new Error("x"))).toBe(2000);
    expect(retryDelay(10, new Error("x"))).toBe(8000);
  });
});

describe("useCurrentSession", () => {
  function Current() {
    const query = useCurrentSession();
    // isSuccess rather than !isLoading: only the success branch narrows `data` away from
    // undefined, since a failed query is neither loading nor holding data.
    if (!query.isSuccess) return <div>loading</div>;
    return <div>{query.data === null ? "no session" : `session ${query.data.id}`}</div>;
  }

  test("resolves to null when the API answers 204", async () => {
    render(harness(<Current />));

    // 204 is data, not an error: "no workout in progress" is the normal state between workouts.
    expect(await screen.findByText("no session")).toBeInTheDocument();
  });

  test("resolves to the session once one is open", async () => {
    await fetch("/api/sessions", {
      method: "POST",
      body: JSON.stringify({}),
      headers: { "content-type": "application/json" },
    });

    render(harness(<Current />));

    expect(await screen.findByText(`session ${IDS.sessionId}`)).toBeInTheDocument();
  });
});

describe("useSession", () => {
  function One({ id }: { id: string }) {
    const query = useSession(id);
    if (query.isError) return <div>error {(query.error as ApiError).errorCode}</div>;
    if (!query.isSuccess) return <div>loading</div>;
    return <div>loaded {query.data.id}</div>;
  }

  test("retries a 500 and resolves when the retry succeeds", async () => {
    let calls = 0;
    server.use(
      http.get("/api/sessions/:id", () => {
        calls += 1;
        if (calls === 1) return new HttpResponse(null, { status: 500 });
        return HttpResponse.json({
          id: IDS.sessionId,
          status: "InProgress",
          startedAt: "2026-11-23T09:00:00Z",
          completedAt: null,
          programId: null,
          programRevision: null,
          programDayId: null,
          bodyweightAtSession: null,
          workingSetCount: 0,
          exercises: [],
        });
      }),
    );

    render(harness(<One id={IDS.sessionId} />));

    expect(await screen.findByText(`loaded ${IDS.sessionId}`)).toBeInTheDocument();
    expect(calls).toBe(2);
  });

  test("does not retry a 404", async () => {
    let calls = 0;
    server.use(
      http.get("/api/sessions/:id", () => {
        calls += 1;
        return HttpResponse.json(
          { title: "gone", status: 404, errorCode: "session.not_found" },
          { status: 404 },
        );
      }),
    );

    render(harness(<One id="00000000-0000-4000-8000-000000009999" />));

    await screen.findByText("error session.not_found");
    expect(calls).toBe(1);
  });
});

describe("useExerciseNames", () => {
  function Names({ ids }: { ids: string[] }) {
    const { nameFor, isLoading } = useExerciseNames();
    if (isLoading) return <div>loading</div>;
    return (
      <ul>
        {ids.map((id) => (
          <li key={id}>{nameFor(id)}</li>
        ))}
      </ul>
    );
  }

  test("resolves ids to names", async () => {
    render(harness(<Names ids={[IDS.benchExerciseId, IDS.inclineExerciseId]} />));

    expect(await screen.findByText("Bench press")).toBeInTheDocument();
    expect(screen.getByText("Incline DB press")).toBeInTheDocument();
  });

  test("returns a readable placeholder for an id missing from the catalog", async () => {
    render(harness(<Names ids={["00000000-0000-4000-8000-0000000000ff"]} />));

    // An unnamed exercise is cosmetic. Throwing here would take down the whole workout screen.
    expect(await screen.findByText("Unknown exercise")).toBeInTheDocument();
  });

  test("fetches the catalog once across two components sharing a client", async () => {
    let calls = 0;
    server.use(
      http.get("/api/exercises", () => {
        calls += 1;
        return HttpResponse.json([
          {
            id: IDS.benchExerciseId,
            slug: "bench",
            name: "Bench press",
            equipment: "Barbell",
            primaryMuscles: ["Chest"],
          },
        ]);
      }),
    );

    render(
      harness(
        <>
          <Names ids={[IDS.benchExerciseId]} />
          <Names ids={[IDS.benchExerciseId]} />
        </>,
      ),
    );

    await waitFor(() => expect(screen.getAllByText("Bench press")).toHaveLength(2));
    expect(calls).toBe(1);
  });
});

describe("useProfile", () => {
  function Unit() {
    const query = useProfile();
    if (!query.isSuccess) return <div>loading</div>;
    return <div>{`${query.data.preferredUnit} @ ${query.data.loadingIncrement}`}</div>;
  }

  test("provides the unit and increment every weight display depends on", async () => {
    render(harness(<Unit />));

    expect(await screen.findByText("Kilograms @ 2.5")).toBeInTheDocument();
  });
});
