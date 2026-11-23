import type { components } from "../api/generated";

type SessionExercise = components["schemas"]["SessionExerciseResponse"];
type LoggedSet = components["schemas"]["LoggedSetResponse"];

export type SetKind = components["schemas"]["SetKind"];
export type WeightUnit = components["schemas"]["WeightUnit"];

export type RowStatus = "pending" | "inFlight" | "logged" | "failed";

export interface SetRow {
  /**
   * Stable across renders and remounts, because it keys the idempotency map: the same logical set
   * must always resolve to the same clientSetId, or a re-tap after a lost response would create a
   * duplicate.
   */
  rowKey: string;
  kind: SetKind;
  /** 0-based within its own kind. */
  ordinal: number;
  weight: number;
  reps: number;
  rir: number | null;
  /** The unit this row's weight is expressed in. Never converted — see below. */
  unit: WeightUnit;
  status: RowStatus;
  /** Warm-up steps only; a working set is never optional. */
  isOptional: boolean;
  loggedSetId: string | null;
  clientSetId: string | null;
  /** Logged beyond the prescribed count. */
  isExtra: boolean;
}

export function rowKeyFor(sessionExerciseId: string, kind: SetKind, ordinal: number): string {
  return `${sessionExerciseId}:${kind}:${ordinal}`;
}

function loggedRow(
  sessionExerciseId: string,
  set: LoggedSet,
  ordinal: number,
  isExtra: boolean,
  isOptional: boolean,
): SetRow {
  return {
    rowKey: rowKeyFor(sessionExerciseId, set.kind, ordinal),
    kind: set.kind,
    ordinal,
    // The ACTUAL numbers, not the prescribed ones. An athlete who pressed 82.5 for 6 needs to see
    // 82.5 for 6, or the screen is lying about their workout.
    weight: set.weight,
    reps: set.reps,
    rir: set.rir,
    // Kept as logged. A set recorded in pounds last month stays in pounds: the client does no unit
    // arithmetic, because a converted-and-rounded number would not be the weight that was lifted.
    unit: set.unit,
    status: "logged",
    isOptional,
    loggedSetId: set.id,
    clientSetId: set.clientSetId,
    isExtra,
  };
}

/**
 * Derives the rows the session screen renders from the two things the server sends: what was
 * prescribed, and what has actually been logged.
 *
 * Nothing about the row model is stored. The screen is a pure function of the server response plus
 * in-flight mutations, so a background refetch can never disagree with what is on screen.
 */
export function deriveSetRows(exercise: SessionExercise, options: { unit: WeightUnit }): SetRow[] {
  const byKind = (kind: SetKind): LoggedSet[] =>
    exercise.sets.filter((set) => set.kind === kind).sort((a, b) => a.order - b.order);

  const loggedWarmups = byKind("Warmup");
  const loggedWorking = byKind("Working");

  const rows: SetRow[] = [];

  // ── Warm-ups: one row per prescribed step ────────────────────────────────
  exercise.suggestedWarmup.forEach((step, ordinal) => {
    const logged = loggedWarmups[ordinal];
    rows.push(
      logged !== undefined
        ? loggedRow(exercise.id, logged, ordinal, false, step.isOptional)
        : {
            rowKey: rowKeyFor(exercise.id, "Warmup", ordinal),
            kind: "Warmup",
            ordinal,
            weight: step.weight,
            reps: step.reps,
            rir: null,
            unit: options.unit,
            status: "pending",
            isOptional: step.isOptional,
            loggedSetId: null,
            clientSetId: null,
            isExtra: false,
          },
    );
  });

  // Warm-ups logged beyond the prescribed ramp — an athlete who took an extra build-up set.
  loggedWarmups.slice(exercise.suggestedWarmup.length).forEach((set, index) => {
    const ordinal = exercise.suggestedWarmup.length + index;
    rows.push(loggedRow(exercise.id, set, ordinal, true, false));
  });

  // ── Working sets: one row per prescribed set ─────────────────────────────
  // A null suggested weight means there is no history to suggest from. 0 renders as a stepper the
  // athlete types into, which is honest; NaN or a crash would not be.
  const suggested = exercise.suggestedWorkingWeight ?? 0;

  for (let ordinal = 0; ordinal < exercise.workingSetCount; ordinal += 1) {
    const logged = loggedWorking[ordinal];
    rows.push(
      logged !== undefined
        ? loggedRow(exercise.id, logged, ordinal, false, false)
        : {
            rowKey: rowKeyFor(exercise.id, "Working", ordinal),
            kind: "Working",
            ordinal,
            weight: suggested,
            // The bottom of the range: the number the athlete is certain to hit, and adjusting up
            // is one tap on a number already in front of them.
            reps: exercise.targetRepMin,
            rir: null,
            unit: options.unit,
            status: "pending",
            isOptional: false,
            loggedSetId: null,
            clientSetId: null,
            isExtra: false,
          },
    );
  }

  loggedWorking.slice(exercise.workingSetCount).forEach((set, index) => {
    const ordinal = exercise.workingSetCount + index;
    rows.push(loggedRow(exercise.id, set, ordinal, true, false));
  });

  return rows;
}

/** True once every prescribed set — warm-ups included — has been logged. */
export function isExerciseComplete(rows: SetRow[]): boolean {
  return rows.every((row) => row.status === "logged" || row.isOptional);
}

export function loggedWorkingSetCount(rows: SetRow[]): number {
  return rows.filter((row) => row.kind === "Working" && row.status === "logged").length;
}

export function prescribedWorkingSetCount(rows: SetRow[]): number {
  return rows.filter((row) => row.kind === "Working" && !row.isExtra).length;
}
