import type { components } from "../../src/api/generated";

type Profile = components["schemas"]["ProfileResponse"];
type ExerciseSummary = components["schemas"]["ExerciseSummary"];
type Program = components["schemas"]["ProgramResponse"];
type ProgramSummary = components["schemas"]["ProgramSummaryResponse"];
type DayPreview = components["schemas"]["DayPreviewResponse"];
type Session = components["schemas"]["SessionResponse"];
type SessionSummary = components["schemas"]["SessionSummaryResponse"];
type LoggedSet = components["schemas"]["LoggedSetResponse"];
type AuthTokens = components["schemas"]["AuthTokensResponse"];

/**
 * Fixtures are typed as the *generated* response types rather than as structural literals, so a
 * backend rename breaks this file at compile time instead of at runtime in a test that still
 * passes.
 *
 * The data is the athlete's real program, so failures are recognisable: a push day of bench press
 * (3 x 8-10 at 80 kg) and incline dumbbell press (3 x 8-10 at 27.5 kg).
 */

export const IDS = {
  userId: "00000000-0000-4000-8000-000000000001",
  programId: "00000000-0000-4000-8000-000000000010",
  dayId: "00000000-0000-4000-8000-000000000011",
  benchExerciseId: "00000000-0000-4000-8000-000000000020",
  inclineExerciseId: "00000000-0000-4000-8000-000000000021",
  benchPrescriptionId: "00000000-0000-4000-8000-000000000030",
  inclinePrescriptionId: "00000000-0000-4000-8000-000000000031",
  sessionId: "00000000-0000-4000-8000-000000000040",
  benchSessionExerciseId: "00000000-0000-4000-8000-000000000041",
  inclineSessionExerciseId: "00000000-0000-4000-8000-000000000042",
  completedSessionId: "00000000-0000-4000-8000-000000000050",
} as const;

export const profile: Profile = {
  id: IDS.userId,
  firstName: "Akmal",
  username: "akmal",
  languageCode: "en",
  birthYear: 1996,
  bodyweight: 74,
  preferredUnit: "Kilograms",
  loadingIncrement: 2.5,
  experienceLevel: "Intermediate",
  availableDaysPerWeek: 3,
  sessionMinutes: 75,
  timeZoneId: "Asia/Tashkent",
};

export const exercises: ExerciseSummary[] = [
  {
    id: IDS.benchExerciseId,
    slug: "barbell-bench-press",
    name: "Bench press",
    equipment: "Barbell",
    primaryMuscles: ["Chest"],
  },
  {
    id: IDS.inclineExerciseId,
    slug: "incline-dumbbell-press",
    name: "Incline DB press",
    equipment: "Dumbbell",
    primaryMuscles: ["Chest"],
  },
];

/**
 * 27 / 53 / 80 % of 80 kg, rounded DOWN to a 2.5 kg increment: 21.6 -> 20, 42.4 -> 40, 64 -> 62.5.
 * These are the backend's own values, proven end to end in
 * tests/MyGym.IntegrationTests/SessionEndpointTests.cs. They are copied, never recomputed — a
 * round-to-nearest implementation would give 22.5 / 42.5 / 65 and a test that recomputed them
 * would agree with the bug.
 */
const benchWarmup = [
  { weight: 20, reps: 10, isOptional: false },
  { weight: 40, reps: 5, isOptional: false },
  { weight: 62.5, reps: 3, isOptional: true },
];

const inclineWarmup = [
  { weight: 12.5, reps: 10, isOptional: false },
  { weight: 17.5, reps: 5, isOptional: false },
];

export const program: Program = {
  id: IDS.programId,
  name: "Push / Legs / Full body",
  status: "Active",
  visibility: "Private",
  revisionNumber: 1,
  forkedFromProgramId: null,
  days: [
    {
      id: IDS.dayId,
      order: 0,
      label: "Push day",
      dayOfWeek: "Monday",
      prescriptions: [
        {
          id: IDS.benchPrescriptionId,
          order: 0,
          exerciseId: IDS.benchExerciseId,
          alternativeExerciseId: null,
          workingSetCount: 3,
          repMin: 8,
          repMax: 10,
          restMinSeconds: 120,
          restMaxSeconds: 180,
          // Fractions, not percentages: WarmupStep.Create requires 0 < p < 1, so the API takes
          // 0.27 for 27%. The fixture said 27 and nothing caught it, because the preview and
          // session responses carry resolved weights rather than percentages -- exactly the kind
          // of quiet drift between a mock and the real contract this file exists to avoid.
          warmup: [
            { percentOfWorking: 0.27, reps: 10, isOptional: false },
            { percentOfWorking: 0.53, reps: 5, isOptional: false },
            { percentOfWorking: 0.8, reps: 3, isOptional: true },
          ],
          notes: null,
        },
        {
          id: IDS.inclinePrescriptionId,
          order: 1,
          exerciseId: IDS.inclineExerciseId,
          alternativeExerciseId: null,
          workingSetCount: 3,
          repMin: 8,
          repMax: 10,
          restMinSeconds: 90,
          restMaxSeconds: 120,
          warmup: [
            { percentOfWorking: 0.45, reps: 10, isOptional: false },
            { percentOfWorking: 0.63, reps: 5, isOptional: false },
          ],
          notes: null,
        },
      ],
    },
  ],
};

export const programSummaries: ProgramSummary[] = [
  {
    id: IDS.programId,
    name: program.name,
    status: "Active",
    visibility: "Private",
    dayCount: 1,
    isOwnedByMe: true,
  },
];

export const dayPreview: DayPreview = {
  programId: IDS.programId,
  dayId: IDS.dayId,
  label: "Push day",
  revisionNumber: 1,
  exercises: [
    {
      exerciseId: IDS.benchExerciseId,
      alternativeExerciseId: null,
      workingSetCount: 3,
      targetRepMin: 8,
      targetRepMax: 10,
      restMinSeconds: 120,
      restMaxSeconds: 180,
      suggestedWorkingWeight: 80,
      weightUnit: "Kilograms",
      warmup: benchWarmup,
      notes: null,
    },
    {
      exerciseId: IDS.inclineExerciseId,
      alternativeExerciseId: null,
      workingSetCount: 3,
      targetRepMin: 8,
      targetRepMax: 10,
      restMinSeconds: 90,
      restMaxSeconds: 120,
      suggestedWorkingWeight: 27.5,
      weightUnit: "Kilograms",
      warmup: inclineWarmup,
      notes: null,
    },
  ],
};

function freshSession(): Session {
  return {
    id: IDS.sessionId,
    status: "InProgress",
    startedAt: "2026-11-23T09:00:00+00:00",
    completedAt: null,
    programId: IDS.programId,
    programRevision: 1,
    programDayId: IDS.dayId,
    bodyweightAtSession: 74,
    workingSetCount: 0,
    exercises: [
      {
        id: IDS.benchSessionExerciseId,
        order: 0,
        exerciseId: IDS.benchExerciseId,
        alternativeExerciseId: null,
        workingSetCount: 3,
        targetRepMin: 8,
        targetRepMax: 10,
        restMinSeconds: 120,
        restMaxSeconds: 180,
        suggestedWarmup: benchWarmup,
        suggestedWorkingWeight: 80,
        sets: [],
      },
      {
        id: IDS.inclineSessionExerciseId,
        order: 1,
        exerciseId: IDS.inclineExerciseId,
        alternativeExerciseId: null,
        workingSetCount: 3,
        targetRepMin: 8,
        targetRepMax: 10,
        restMinSeconds: 90,
        restMaxSeconds: 120,
        suggestedWarmup: inclineWarmup,
        suggestedWorkingWeight: 27.5,
        sets: [],
      },
    ],
  };
}

export const completedSession: Session = {
  ...freshSession(),
  id: IDS.completedSessionId,
  status: "Completed",
  startedAt: "2026-11-20T09:00:00+00:00",
  completedAt: "2026-11-20T10:12:00+00:00",
  workingSetCount: 6,
};

export const sessionSummaries: SessionSummary[] = [
  {
    id: IDS.completedSessionId,
    status: "Completed",
    startedAt: "2026-11-20T09:00:00+00:00",
    completedAt: "2026-11-20T10:12:00+00:00",
    workingSetCount: 6,
  },
];

export const authTokens: AuthTokens = {
  accessToken: "access-token-fixture",
  refreshToken: "refresh-token-fixture",
  accessTokenExpiresAt: "2026-11-23T09:15:00+00:00",
};

/**
 * Mutable state so that logging a set changes what a later GET returns. Without this, an
 * optimistic-update test proves nothing: the cache patch and the refetch would agree by accident
 * because the server never changed.
 */
interface FixtureState {
  session: Session | null;
  nextSetOrdinal: number;
}

let state: FixtureState = { session: null, nextSetOrdinal: 0 };

export function resetFixtureState(): void {
  state = { session: null, nextSetOrdinal: 0 };
}

export function openSession(): Session {
  state.session = freshSession();
  return state.session;
}

export function currentSession(): Session | null {
  return state.session;
}

export function closeSession(status: "Completed" | "Abandoned"): void {
  if (state.session === null) return;
  state.session = { ...state.session, status, completedAt: "2026-11-23T10:15:00+00:00" };
}

export function workingSetCount(): number {
  if (state.session === null) return 0;
  return state.session.exercises.reduce(
    (total, exercise) => total + exercise.sets.filter((s) => s.kind === "Working").length,
    0,
  );
}

/**
 * Appends a set, or returns the existing one when `clientSetId` has been seen before.
 *
 * This mirrors the backend's unique index on ClientSetId and is the single most important
 * behaviour in this file: it is what makes a "re-tap after a failure does not duplicate" test
 * mean something rather than merely pass.
 */
export function logSet(
  sessionExerciseId: string,
  input: { clientSetId: string; kind: LoggedSet["kind"]; weight: number; reps: number; rir: number | null },
): { set: LoggedSet; replayed: boolean } | null {
  if (state.session === null) return null;

  const exercise = state.session.exercises.find((e) => e.id === sessionExerciseId);
  if (exercise === undefined) return null;

  const existing = exercise.sets.find((s) => s.clientSetId === input.clientSetId);
  if (existing !== undefined) return { set: existing, replayed: true };

  const set: LoggedSet = {
    id: `99999999-0000-4000-8000-${String(state.nextSetOrdinal).padStart(12, "0")}`,
    order: exercise.sets.length,
    kind: input.kind,
    weight: input.weight,
    unit: profile.preferredUnit,
    reps: input.reps,
    rir: input.rir,
    clientSetId: input.clientSetId,
    loggedAt: "2026-11-23T09:05:00+00:00",
  };
  state.nextSetOrdinal += 1;

  // Replace the arrays rather than mutating them, so a consumer holding the previous object does
  // not see it change underneath — the same discipline the real cache requires.
  const updatedExercise = { ...exercise, sets: [...exercise.sets, set] };
  state.session = {
    ...state.session,
    exercises: state.session.exercises.map((e) => (e.id === sessionExerciseId ? updatedExercise : e)),
  };
  state.session = { ...state.session, workingSetCount: workingSetCount() };

  return { set, replayed: false };
}

export function deleteSet(sessionExerciseId: string, setId: string): boolean {
  if (state.session === null) return false;
  const exercise = state.session.exercises.find((e) => e.id === sessionExerciseId);
  if (exercise === undefined || !exercise.sets.some((s) => s.id === setId)) return false;

  state.session = {
    ...state.session,
    exercises: state.session.exercises.map((e) =>
      e.id === sessionExerciseId ? { ...e, sets: e.sets.filter((s) => s.id !== setId) } : e,
    ),
  };
  state.session = { ...state.session, workingSetCount: workingSetCount() };
  return true;
}
