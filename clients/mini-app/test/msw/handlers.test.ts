import { expect, test } from "vitest";
import { IDS } from "./fixtures";

/**
 * The harness has to be trustworthy before anything is tested through it. These cases cover the
 * three contract behaviours the client's design depends on; if any of them regress, later tests
 * would pass while proving nothing.
 */

async function postJson(url: string, body: unknown): Promise<Response> {
  return fetch(url, {
    method: "POST",
    body: JSON.stringify(body),
    headers: { "content-type": "application/json" },
  });
}

test("GET /api/sessions/current answers 204 with no open session and 200 once one is started", async () => {
  expect((await fetch("/api/sessions/current")).status).toBe(204);

  await postJson("/api/sessions", { programId: IDS.programId, programDayId: IDS.dayId });

  const current = await fetch("/api/sessions/current");
  expect(current.status).toBe(200);
  expect(((await current.json()) as { id: string }).id).toBe(IDS.sessionId);
});

test("starting a second session while one is open is a 409 with session.already_in_progress", async () => {
  await postJson("/api/sessions", {});
  const second = await postJson("/api/sessions", {});

  expect(second.status).toBe(409);
  expect(((await second.json()) as { errorCode: string }).errorCode).toBe("session.already_in_progress");
});

test("replaying a set with the same clientSetId returns the original and does not append", async () => {
  const session = (await (await postJson("/api/sessions", {})).json()) as { id: string };
  const url = `/api/sessions/${session.id}/exercises/${IDS.benchSessionExerciseId}/sets`;
  const body = {
    clientSetId: "11111111-1111-1111-1111-111111111111",
    kind: "Working",
    weight: 80,
    reps: 8,
    rir: 2,
  };

  const first = (await (await postJson(url, body)).json()) as { id: string };
  const replay = (await (await postJson(url, body)).json()) as { id: string };

  expect(replay.id).toBe(first.id);

  const after = (await (await fetch(`/api/sessions/${session.id}`)).json()) as {
    exercises: { id: string; sets: unknown[] }[];
  };
  const bench = after.exercises.find((e) => e.id === IDS.benchSessionExerciseId);
  expect(bench?.sets).toHaveLength(1);
});

test("a different clientSetId does append a second set", async () => {
  const session = (await (await postJson("/api/sessions", {})).json()) as { id: string };
  const url = `/api/sessions/${session.id}/exercises/${IDS.benchSessionExerciseId}/sets`;
  const base = { kind: "Working", weight: 80, reps: 8, rir: 2 };

  await postJson(url, { ...base, clientSetId: "11111111-1111-1111-1111-111111111111" });
  await postJson(url, { ...base, clientSetId: "22222222-2222-2222-2222-222222222222" });

  const after = (await (await fetch(`/api/sessions/${session.id}`)).json()) as {
    workingSetCount: number;
    exercises: { id: string; sets: unknown[] }[];
  };
  expect(after.exercises.find((e) => e.id === IDS.benchSessionExerciseId)?.sets).toHaveLength(2);
  expect(after.workingSetCount).toBe(2);
});

test("completing with no working set logged is a 409, and succeeds once one is", async () => {
  const session = (await (await postJson("/api/sessions", {})).json()) as { id: string };

  const tooEarly = await postJson(`/api/sessions/${session.id}/complete`, {});
  expect(tooEarly.status).toBe(409);
  expect(((await tooEarly.json()) as { errorCode: string }).errorCode).toBe(
    "session.needs_at_least_one_working_set",
  );

  await postJson(`/api/sessions/${session.id}/exercises/${IDS.benchSessionExerciseId}/sets`, {
    clientSetId: "33333333-3333-3333-3333-333333333333",
    kind: "Working",
    weight: 80,
    reps: 8,
    rir: null,
  });

  expect((await postJson(`/api/sessions/${session.id}/complete`, {})).status).toBe(204);
});

test("logging into a completed session is a 409 with session.closed", async () => {
  const session = (await (await postJson("/api/sessions", {})).json()) as { id: string };
  const setsUrl = `/api/sessions/${session.id}/exercises/${IDS.benchSessionExerciseId}/sets`;
  await postJson(setsUrl, {
    clientSetId: "44444444-4444-4444-4444-444444444444",
    kind: "Working",
    weight: 80,
    reps: 8,
    rir: null,
  });
  await postJson(`/api/sessions/${session.id}/complete`, {});

  const late = await postJson(setsUrl, {
    clientSetId: "55555555-5555-5555-5555-555555555555",
    kind: "Working",
    weight: 80,
    reps: 8,
    rir: null,
  });

  expect(late.status).toBe(409);
  expect(((await late.json()) as { errorCode: string }).errorCode).toBe("session.closed");
});

test("a warm-up set does not count toward the working-set total", async () => {
  const session = (await (await postJson("/api/sessions", {})).json()) as { id: string };
  await postJson(`/api/sessions/${session.id}/exercises/${IDS.benchSessionExerciseId}/sets`, {
    clientSetId: "66666666-6666-6666-6666-666666666666",
    kind: "Warmup",
    weight: 20,
    reps: 10,
    rir: null,
  });

  const after = (await (await fetch(`/api/sessions/${session.id}`)).json()) as { workingSetCount: number };
  expect(after.workingSetCount).toBe(0);
});

test("fixture state is reset between tests, so no session leaks from the cases above", async () => {
  expect((await fetch("/api/sessions/current")).status).toBe(204);
});

test("failures carry the ProblemDetails errorCode extension the client branches on", async () => {
  const response = await fetch("/api/programs/00000000-0000-4000-8000-000000009999");
  expect(response.status).toBe(404);
  expect(response.headers.get("content-type")).toContain("problem+json");

  const body = (await response.json()) as { errorCode: string; title: string; type: string };
  expect(body.errorCode).toBe("program.not_found");
  expect(body.type).toBe("https://mygym.app/errors/program.not_found");
  expect(body.title.length).toBeGreaterThan(0);
});

test("the day preview serves the backend's round-down warm-up weights", async () => {
  const preview = (await (
    await fetch(`/api/programs/${IDS.programId}/days/${IDS.dayId}/preview`)
  ).json()) as { exercises: { warmup: { weight: number }[] }[] };

  // 27 / 53 / 80 % of 80 kg on a 2.5 kg increment, rounded down. Round-to-nearest would be
  // 22.5 / 42.5 / 65, so this fixture also guards the direction.
  expect(preview.exercises[0]!.warmup.map((w) => w.weight)).toEqual([20, 40, 62.5]);
});
