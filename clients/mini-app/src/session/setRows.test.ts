import { describe, expect, test } from "vitest";
import type { components } from "../api/generated";
import {
  deriveSetRows,
  isExerciseComplete,
  loggedWorkingSetCount,
  prescribedWorkingSetCount,
  rowKeyFor,
} from "./setRows";

type Exercise = components["schemas"]["SessionExerciseResponse"];
type LoggedSet = components["schemas"]["LoggedSetResponse"];

function exercise(over: Partial<Exercise> = {}): Exercise {
  return {
    id: "se1",
    order: 0,
    exerciseId: "ex1",
    alternativeExerciseId: null,
    workingSetCount: 3,
    targetRepMin: 8,
    targetRepMax: 10,
    restMinSeconds: 120,
    restMaxSeconds: 180,
    suggestedWarmup: [
      { weight: 20, reps: 10, isOptional: false },
      { weight: 40, reps: 5, isOptional: false },
      { weight: 62.5, reps: 3, isOptional: true },
    ],
    suggestedWorkingWeight: 80,
    sets: [],
    ...over,
  };
}

function set(over: Partial<LoggedSet> = {}): LoggedSet {
  return {
    id: "s1",
    order: 0,
    kind: "Working",
    weight: 80,
    unit: "Kilograms",
    reps: 8,
    rir: null,
    clientSetId: "c1",
    loggedAt: "2026-11-23T10:00:00Z",
    ...over,
  };
}

const kg = { unit: "Kilograms" } as const;

describe("deriveSetRows", () => {
  test("produces one row per warm-up step then one per prescribed working set", () => {
    const rows = deriveSetRows(exercise(), kg);

    expect(rows.map((r) => r.kind)).toEqual([
      "Warmup",
      "Warmup",
      "Warmup",
      "Working",
      "Working",
      "Working",
    ]);
  });

  test("carries the backend's round-down warm-up weights through unchanged", () => {
    const rows = deriveSetRows(exercise(), kg);

    // 27 / 53 / 80 % of 80 kg rounded DOWN on a 2.5 kg increment. Round-to-nearest would be
    // 22.5 / 42.5 / 65, so this also guards the direction against a client that recomputed.
    expect(rows.filter((r) => r.kind === "Warmup").map((r) => r.weight)).toEqual([20, 40, 62.5]);
  });

  test("pre-fills working rows at the suggested weight and the bottom of the rep range", () => {
    const working = deriveSetRows(exercise(), kg).filter((r) => r.kind === "Working");

    expect(working.every((r) => r.weight === 80 && r.reps === 8)).toBe(true);
    expect(working.every((r) => r.status === "pending" && r.rir === null)).toBe(true);
  });

  test("a logged set replaces its row and shows the ACTUAL numbers, not the prescribed ones", () => {
    const rows = deriveSetRows(
      exercise({ sets: [set({ weight: 82.5, reps: 6, rir: 1, id: "logged-1" })] }),
      kg,
    );

    const first = rows.find((r) => r.kind === "Working");
    expect(first?.status).toBe("logged");
    expect(first?.weight).toBe(82.5);
    expect(first?.reps).toBe(6);
    expect(first?.rir).toBe(1);
    expect(first?.loggedSetId).toBe("logged-1");
    expect(first?.clientSetId).toBe("c1");
  });

  test("warm-up and working sets are matched within their own kind, never across kinds", () => {
    const rows = deriveSetRows(
      exercise({ sets: [set({ kind: "Warmup", weight: 20, reps: 10, id: "w1" })] }),
      kg,
    );

    expect(rows.filter((r) => r.status === "logged")).toHaveLength(1);
    expect(rows.find((r) => r.status === "logged")?.kind).toBe("Warmup");
    expect(rows.filter((r) => r.kind === "Working").every((r) => r.status === "pending")).toBe(true);
  });

  test("logged sets are matched by order, not by array position", () => {
    // The API returns sets in insertion order, but a client must not depend on that.
    const rows = deriveSetRows(
      exercise({
        sets: [
          set({ id: "second", order: 1, weight: 85, clientSetId: "c2" }),
          set({ id: "first", order: 0, weight: 80, clientSetId: "c1" }),
        ],
      }),
      kg,
    );

    const working = rows.filter((r) => r.kind === "Working");
    expect(working[0]?.loggedSetId).toBe("first");
    expect(working[1]?.loggedSetId).toBe("second");
  });

  test("working sets logged beyond the prescribed count appear as extra rows after them", () => {
    const sets = [0, 1, 2, 3].map((i) =>
      set({ id: `s${i}`, order: i, clientSetId: `c${i}` }),
    );
    const working = deriveSetRows(exercise({ sets }), kg).filter((r) => r.kind === "Working");

    expect(working).toHaveLength(4);
    expect(working.slice(0, 3).every((r) => !r.isExtra)).toBe(true);
    expect(working[3]?.isExtra).toBe(true);
  });

  test("warm-ups logged beyond the prescribed ramp also appear as extra rows", () => {
    const sets = [0, 1, 2, 3].map((i) =>
      set({ id: `w${i}`, order: i, kind: "Warmup", clientSetId: `cw${i}` }),
    );
    const warmups = deriveSetRows(exercise({ sets }), kg).filter((r) => r.kind === "Warmup");

    expect(warmups).toHaveLength(4);
    expect(warmups[3]?.isExtra).toBe(true);
  });

  test("row keys are unique and stable, because they key the idempotency map", () => {
    const first = deriveSetRows(exercise(), kg);
    const second = deriveSetRows(exercise(), kg);

    expect(new Set(first.map((r) => r.rowKey)).size).toBe(first.length);
    expect(first.map((r) => r.rowKey)).toEqual(second.map((r) => r.rowKey));
    expect(first[3]?.rowKey).toBe("se1:Working:0");
    expect(first[0]?.rowKey).toBe("se1:Warmup:0");
  });

  test("a row's key does not change when the set is logged", () => {
    // If it did, the id map would lose track of an in-flight write the moment it succeeded.
    const before = deriveSetRows(exercise(), kg).find((r) => r.kind === "Working");
    const after = deriveSetRows(exercise({ sets: [set()] }), kg).find((r) => r.kind === "Working");

    expect(after?.rowKey).toBe(before?.rowKey);
  });

  test("rowKeyFor matches the keys deriveSetRows produces", () => {
    const rows = deriveSetRows(exercise(), kg);

    expect(rows.find((r) => r.rowKey === rowKeyFor("se1", "Working", 2))).toBeDefined();
    expect(rows.find((r) => r.rowKey === rowKeyFor("se1", "Warmup", 1))).toBeDefined();
  });

  test("carries isOptional from the warm-up step and never marks a working set optional", () => {
    const rows = deriveSetRows(exercise(), kg);

    expect(rows.filter((r) => r.kind === "Warmup").map((r) => r.isOptional)).toEqual([false, false, true]);
    expect(rows.filter((r) => r.kind === "Working").every((r) => !r.isOptional)).toBe(true);
  });

  test("a null suggested working weight yields rows at 0 rather than NaN or a crash", () => {
    const working = deriveSetRows(exercise({ suggestedWorkingWeight: null }), kg).filter(
      (r) => r.kind === "Working",
    );

    expect(working.every((r) => r.weight === 0)).toBe(true);
    expect(working.every((r) => Number.isFinite(r.weight))).toBe(true);
  });

  test("an exercise with no warm-up ramp produces only working rows", () => {
    const rows = deriveSetRows(exercise({ suggestedWarmup: [] }), kg);

    expect(rows).toHaveLength(3);
    expect(rows.every((r) => r.kind === "Working")).toBe(true);
  });

  test("a set logged in the other unit keeps its own unit and is not converted", () => {
    const rows = deriveSetRows(
      exercise({ sets: [set({ weight: 180, unit: "Pounds" })] }),
      kg,
    );

    const logged = rows.find((r) => r.status === "logged");
    // 180 lb is about 81.6 kg. A converted-and-rounded number would not be the weight that was
    // lifted, so the row is labelled in pounds instead.
    expect(logged?.weight).toBe(180);
    expect(logged?.unit).toBe("Pounds");
  });

  test("pending rows take the athlete's current unit", () => {
    const rows = deriveSetRows(exercise(), { unit: "Pounds" });

    expect(rows.every((r) => r.unit === "Pounds")).toBe(true);
  });

  test("a zero prescribed working-set count still renders the warm-up ramp", () => {
    const rows = deriveSetRows(exercise({ workingSetCount: 0 }), kg);

    expect(rows).toHaveLength(3);
    expect(rows.every((r) => r.kind === "Warmup")).toBe(true);
  });
});

describe("row summaries", () => {
  test("isExerciseComplete ignores an unlogged optional warm-up", () => {
    const sets = [
      set({ id: "w0", order: 0, kind: "Warmup", clientSetId: "cw0" }),
      set({ id: "w1", order: 1, kind: "Warmup", clientSetId: "cw1" }),
      set({ id: "s0", order: 0, clientSetId: "c0" }),
      set({ id: "s1", order: 1, clientSetId: "c1" }),
      set({ id: "s2", order: 2, clientSetId: "c2" }),
    ];

    // The third warm-up step is optional and unlogged; the exercise is still done.
    expect(isExerciseComplete(deriveSetRows(exercise({ sets }), kg))).toBe(true);
  });

  test("isExerciseComplete is false while a required row is pending", () => {
    expect(isExerciseComplete(deriveSetRows(exercise(), kg))).toBe(false);
  });

  test("counts logged and prescribed working sets, excluding extras from the prescribed total", () => {
    const sets = [0, 1, 2, 3].map((i) => set({ id: `s${i}`, order: i, clientSetId: `c${i}` }));
    const rows = deriveSetRows(exercise({ sets }), kg);

    expect(loggedWorkingSetCount(rows)).toBe(4);
    expect(prescribedWorkingSetCount(rows)).toBe(3);
  });

  test("a logged warm-up does not count toward the working-set total", () => {
    const rows = deriveSetRows(
      exercise({ sets: [set({ kind: "Warmup", id: "w0", clientSetId: "cw0" })] }),
      kg,
    );

    expect(loggedWorkingSetCount(rows)).toBe(0);
  });
});
