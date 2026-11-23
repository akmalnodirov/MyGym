import { http, HttpResponse, type HttpHandler } from "msw";
import type { components } from "../../src/api/generated";
import * as fx from "./fixtures";

type LogSetRequest = components["schemas"]["LogSetRequest"];
type StartSessionRequest = components["schemas"]["StartSessionRequest"];
type UpdateProfileRequest = components["schemas"]["UpdateProfileRequest"];

/**
 * Handlers mirror the real API closely enough that a test passing here means something. In
 * particular they reproduce the three contract details the client is built around:
 *
 *   - GET /api/sessions/current answers 204 when no workout is open.
 *   - POST .../sets is idempotent on clientSetId: a replay returns the original set.
 *   - Failures are ProblemDetails with an `errorCode` extension.
 */

/** The API's error shape, so a test never accidentally asserts against a different one. */
function problem(status: number, errorCode: string, title: string): Response {
  return HttpResponse.json(
    { title, status, type: `https://mygym.app/errors/${errorCode}`, errorCode },
    { status, headers: { "content-type": "application/problem+json" } },
  );
}

let mutableProfile = { ...fx.profile };

export function resetProfile(): void {
  mutableProfile = { ...fx.profile };
}

export const handlers: HttpHandler[] = [
  // ── Authentication ──────────────────────────────────────────────────────
  http.post("/api/auth/telegram", async ({ request }) => {
    const body = (await request.json()) as { initData?: string };
    if (body.initData === undefined || body.initData.length === 0) {
      return problem(400, "auth.init_data_malformed", "initData is required.");
    }
    if (body.initData === "invalid") {
      return problem(401, "auth.init_data_signature_invalid", "The initData signature did not verify.");
    }
    return HttpResponse.json(fx.authTokens);
  }),

  http.post("/api/auth/refresh", async ({ request }) => {
    const body = (await request.json()) as { refreshToken?: string };
    if (body.refreshToken === "expired") {
      return problem(401, "auth.refresh_token_invalid", "That refresh token is no longer valid.");
    }
    return HttpResponse.json({ ...fx.authTokens, accessToken: "access-token-refreshed" });
  }),

  // ── Profile ─────────────────────────────────────────────────────────────
  http.get("/api/me", () => HttpResponse.json(mutableProfile)),

  http.patch("/api/me", async ({ request }) => {
    const body = (await request.json()) as UpdateProfileRequest;
    if (body.timeZoneId === "Mars/Olympus_Mons") {
      return problem(422, "profile.time_zone_unknown", "That time zone is not recognised.");
    }
    // PATCH /api/me is a full replace on the real API, so the handler replaces rather than merges —
    // a client that omits a field must see it cleared here too.
    mutableProfile = {
      ...mutableProfile,
      birthYear: body.birthYear,
      bodyweight: body.bodyweight,
      preferredUnit: body.preferredUnit,
      loadingIncrement: body.loadingIncrement,
      experienceLevel: body.experienceLevel,
      availableDaysPerWeek: body.availableDaysPerWeek,
      sessionMinutes: body.sessionMinutes,
      timeZoneId: body.timeZoneId,
    };
    return HttpResponse.json(mutableProfile);
  }),

  http.delete("/api/me", () => new HttpResponse(null, { status: 204 })),

  // ── Catalog ─────────────────────────────────────────────────────────────
  http.get("/api/exercises", ({ request }) => {
    const term = new URL(request.url).searchParams.get("q");
    if (term === null || term.length === 0) return HttpResponse.json(fx.exercises);
    const lower = term.toLowerCase();
    return HttpResponse.json(fx.exercises.filter((e) => e.name.toLowerCase().includes(lower)));
  }),

  // ── Programs ────────────────────────────────────────────────────────────
  http.get("/api/programs", () => HttpResponse.json(fx.programSummaries)),

  http.get("/api/programs/:id", ({ params }) =>
    params["id"] === fx.IDS.programId
      ? HttpResponse.json(fx.program)
      : problem(404, "program.not_found", "That program does not exist."),
  ),

  http.get("/api/programs/:id/days/:dayId/preview", ({ params }) => {
    if (params["id"] !== fx.IDS.programId) {
      return problem(404, "program.not_found", "That program does not exist.");
    }
    if (params["dayId"] !== fx.IDS.dayId) {
      return problem(404, "program.day_not_found", "That training day does not exist.");
    }
    return HttpResponse.json(fx.dayPreview);
  }),

  // ── Sessions ────────────────────────────────────────────────────────────
  http.get("/api/sessions/current", () => {
    const session = fx.currentSession();
    if (session === null || session.status !== "InProgress") {
      // 204, not an empty 200 and not a 404: "no workout in progress" is a normal state.
      return new HttpResponse(null, { status: 204 });
    }
    return HttpResponse.json(session);
  }),

  http.post("/api/sessions", async ({ request }) => {
    const open = fx.currentSession();
    if (open !== null && open.status === "InProgress") {
      return problem(409, "session.already_in_progress", "A workout is already in progress.");
    }
    await request.json().catch(() => ({}) as StartSessionRequest);
    const session = fx.openSession();
    return HttpResponse.json(session, {
      status: 201,
      headers: { location: `/api/sessions/${session.id}` },
    });
  }),

  http.get("/api/sessions", () => HttpResponse.json(fx.sessionSummaries)),

  http.get("/api/sessions/:id", ({ params }) => {
    const session = fx.currentSession();
    if (session !== null && params["id"] === session.id) return HttpResponse.json(session);
    if (params["id"] === fx.IDS.completedSessionId) return HttpResponse.json(fx.completedSession);
    return problem(404, "session.not_found", "That workout does not exist.");
  }),

  http.post("/api/sessions/:id/exercises/:exerciseId/sets", async ({ params, request }) => {
    const session = fx.currentSession();
    if (session === null || params["id"] !== session.id) {
      return problem(404, "session.not_found", "That workout does not exist.");
    }
    if (session.status !== "InProgress") {
      return problem(409, "session.closed", "That workout is already finished.");
    }

    const body = (await request.json()) as LogSetRequest;
    const result = fx.logSet(String(params["exerciseId"]), {
      clientSetId: body.clientSetId,
      kind: body.kind,
      weight: body.weight,
      reps: body.reps,
      rir: body.rir,
    });

    if (result === null) {
      return problem(404, "session.exercise_not_found", "That exercise is not in this workout.");
    }

    // 201 for both the first write and a replay. The real API answers a replay with the original
    // set rather than a conflict, which is what makes a client retry safe.
    return HttpResponse.json(result.set, { status: 201 });
  }),

  http.patch("/api/sessions/:id/exercises/:exerciseId/sets/:setId", () => new HttpResponse(null, { status: 204 })),

  http.delete("/api/sessions/:id/exercises/:exerciseId/sets/:setId", ({ params }) =>
    fx.deleteSet(String(params["exerciseId"]), String(params["setId"]))
      ? new HttpResponse(null, { status: 204 })
      : problem(404, "set.not_found", "That set does not exist."),
  ),

  http.post("/api/sessions/:id/complete", ({ params }) => {
    const session = fx.currentSession();
    if (session === null || params["id"] !== session.id) {
      return problem(404, "session.not_found", "That workout does not exist.");
    }
    if (session.status !== "InProgress") {
      return problem(409, "session.closed", "That workout is already finished.");
    }
    if (fx.workingSetCount() === 0) {
      return problem(
        409,
        "session.needs_at_least_one_working_set",
        "Log at least one working set before finishing.",
      );
    }
    fx.closeSession("Completed");
    return new HttpResponse(null, { status: 204 });
  }),

  http.post("/api/sessions/:id/abandon", ({ params }) => {
    const session = fx.currentSession();
    if (session === null || params["id"] !== session.id) {
      return problem(404, "session.not_found", "That workout does not exist.");
    }
    fx.closeSession("Abandoned");
    return new HttpResponse(null, { status: 204 });
  }),
];
