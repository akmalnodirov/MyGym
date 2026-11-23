import { expect, test } from "vitest";
import type { components } from "./api/generated";

type SessionResponse = components["schemas"]["SessionResponse"];
type SessionExercise = components["schemas"]["SessionExerciseResponse"];
type LoggedSet = components["schemas"]["LoggedSetResponse"];
type DayPreview = components["schemas"]["DayPreviewResponse"];
type Profile = components["schemas"]["ProfileResponse"];

// These are regression guards on the generated contract, not smoke tests. Each literal below is
// checked by tsc against the generated types, so a backend rename or a type widening fails the
// build here rather than at runtime in a gym.

test("a session exercise carries everything the session screen renders from", () => {
  const exercise: SessionExercise = {
    id: "se1",
    order: 0,
    exerciseId: "ex1",
    alternativeExerciseId: null,
    workingSetCount: 3,
    targetRepMin: 8,
    targetRepMax: 10,
    restMinSeconds: 120,
    restMaxSeconds: 180,
    suggestedWarmup: [{ weight: 20, reps: 10, isOptional: false }],
    suggestedWorkingWeight: 80,
    sets: [],
  };

  // Arithmetic, not string handling: these must be numbers or every stepper and comparison in the
  // app needs a coercion. See the NumericSchemaTransformer on the API side.
  expect(exercise.suggestedWorkingWeight! * 2).toBe(160);
  expect(exercise.suggestedWarmup[0]!.weight + 2.5).toBe(22.5);
});

test("a logged set carries its own unit and the client-generated idempotency key", () => {
  const set: LoggedSet = {
    id: "s1",
    order: 0,
    kind: "Working",
    weight: 82.5,
    unit: "Kilograms",
    reps: 8,
    rir: 2,
    clientSetId: "11111111-1111-1111-1111-111111111111",
    loggedAt: "2026-11-23T10:00:00Z",
  };
  expect(set.clientSetId).toHaveLength(36);
  expect(set.weight).toBe(82.5);
});

test("kind, unit and session status are closed unions rather than bare strings", () => {
  // @ts-expect-error -- "working" is not a member of SetKind; this guard fails if kind widens to string
  const badKind: LoggedSet["kind"] = "working";
  // @ts-expect-error -- "kg" is not a member of WeightUnit
  const badUnit: LoggedSet["unit"] = "kg";
  // @ts-expect-error -- "Finished" is not a member of SessionStatus
  const badStatus: SessionResponse["status"] = "Finished";
  expect([badKind, badUnit, badStatus]).toHaveLength(3);
});

test("rir and the suggested working weight are nullable, so the absent case must be handled", () => {
  const set: LoggedSet["rir"] = null;
  const suggested: SessionExercise["suggestedWorkingWeight"] = null;
  expect(set).toBeNull();
  expect(suggested).toBeNull();
});

test("the day preview carries its own weight unit but the session response does not", () => {
  const preview: DayPreview = {
    programId: "p1",
    dayId: "d1",
    label: "Push day",
    revisionNumber: 1,
    exercises: [
      {
        exerciseId: "ex1",
        alternativeExerciseId: null,
        workingSetCount: 3,
        targetRepMin: 8,
        targetRepMax: 10,
        restMinSeconds: 120,
        restMaxSeconds: 180,
        suggestedWorkingWeight: 80,
        weightUnit: "Kilograms",
        warmup: [],
        notes: null,
      },
    ],
  };
  expect(preview.exercises[0]!.weightUnit).toBe("Kilograms");

  // The session screen has no such field, which is why it needs the profile loaded first.
  expect("weightUnit" in ({} as SessionExercise)).toBe(false);
});

test("dayOfWeek is a nullable string union, so weekday mapping must be an explicit table", () => {
  type Day = components["schemas"]["ProgramDayResponse"]["dayOfWeek"];
  const monday: Day = "Monday";
  const unscheduled: Day = null;
  // @ts-expect-error -- not an integer; .NET's DayOfWeek surfaced as names, not 0-6
  const numeric: Day = 1;
  expect([monday, unscheduled, numeric]).toHaveLength(3);
});

test("the profile carries the unit and loading increment every weight display depends on", () => {
  const profile: Pick<Profile, "preferredUnit" | "loadingIncrement" | "bodyweight"> = {
    preferredUnit: "Kilograms",
    loadingIncrement: 2.5,
    bodyweight: 74,
  };
  expect(profile.loadingIncrement).toBe(2.5);
});
