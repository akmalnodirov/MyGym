import { useMutation, useQueryClient, type UseMutationResult } from "@tanstack/react-query";
import type { components } from "./generated";
import { ApiError } from "./ApiError";
import { useApi } from "../auth/AuthProvider";
import { usePendingSetIds } from "../session/PendingSetIdsProvider";
import type { SetKind, WeightUnit } from "../session/setRows";
import { qk } from "./queryKeys";

type Session = components["schemas"]["SessionResponse"];
type LoggedSet = components["schemas"]["LoggedSetResponse"];
type Profile = components["schemas"]["ProfileResponse"];
type UpdateProfileRequest = components["schemas"]["UpdateProfileRequest"];

export interface LogSetVars {
  sessionExerciseId: string;
  rowKey: string;
  kind: SetKind;
  weight: number;
  reps: number;
  rir: number | null;
  /**
   * The unit `weight` is expressed in — the athlete's current preference, from the profile.
   *
   * Supplied by the caller rather than guessed here. The server echoes the unit back on the real
   * LoggedSetResponse, but the optimistic row has to render before that arrives, and labelling an
   * optimistic set with the wrong unit would show the athlete a weight they did not lift.
   */
  unit: WeightUnit;
}

interface LogSetContext {
  previous: Session | undefined;
  clientSetId: string;
}

/** The id of an optimistic set, before the server has assigned a real one. */
const OPTIMISTIC_ID = "";

function appendSet(session: Session, sessionExerciseId: string, set: LoggedSet): Session {
  return {
    ...session,
    workingSetCount:
      set.kind === "Working" ? session.workingSetCount + 1 : session.workingSetCount,
    exercises: session.exercises.map((exercise) =>
      exercise.id === sessionExerciseId ? { ...exercise, sets: [...exercise.sets, set] } : exercise,
    ),
  };
}

/** Removes the set carrying `clientSetId`, matched by id rather than by position. */
function removeSet(session: Session, clientSetId: string): Session {
  return {
    ...session,
    exercises: session.exercises.map((exercise) => ({
      ...exercise,
      sets: exercise.sets.filter((set) => set.clientSetId !== clientSetId),
    })),
  };
}

/**
 * Puts the server's confirmed set into the cache, replacing the optimistic entry with the same
 * `clientSetId` if it is still there and appending it if it is not.
 *
 * The append case is not hypothetical: a refetch that lands while the write is in flight replaces
 * the cache with the server's state, which does not yet contain this set, and a replace-only
 * implementation would then silently drop a set the server had accepted. Matching on
 * `clientSetId` rather than on array position is what makes the two copies recognisably the same
 * set in the first place.
 */
function upsertSet(
  session: Session,
  sessionExerciseId: string,
  clientSetId: string,
  confirmed: LoggedSet,
): Session {
  const present = session.exercises.some((exercise) =>
    exercise.sets.some((set) => set.clientSetId === clientSetId),
  );

  if (!present) return appendSet(session, sessionExerciseId, confirmed);

  return {
    ...session,
    exercises: session.exercises.map((exercise) => ({
      ...exercise,
      sets: exercise.sets.map((set) => (set.clientSetId === clientSetId ? confirmed : set)),
    })),
  };
}

/**
 * Logs one set, optimistically.
 *
 * The row ticks over before the network answers, because the athlete is already racking the bar.
 * If the write fails it stays on screen marked unsynced rather than vanishing — removing a set the
 * athlete actually performed is worse than showing it as not yet saved, and they have the number
 * in their head for about thirty seconds.
 */
export function useLogSet(
  sessionId: string,
  options?: { onLogged?: (vars: LogSetVars) => void },
): UseMutationResult<LoggedSet, ApiError, LogSetVars, LogSetContext> {
  const api = useApi();
  const queryClient = useQueryClient();
  const pending = usePendingSetIds();

  return useMutation<LoggedSet, ApiError, LogSetVars, LogSetContext>({
    mutationFn: (vars) => {
      // idFor, not randomUUID: a manual re-tap after a failure must send the SAME id, or a lost
      // response becomes a duplicated set.
      const clientSetId = pending.idFor(vars.rowKey);

      return api.request<LoggedSet>(
        `/sessions/${sessionId}/exercises/${vars.sessionExerciseId}/sets`,
        {
          method: "POST",
          json: {
            clientSetId,
            kind: vars.kind,
            weight: vars.weight,
            reps: vars.reps,
            rir: vars.rir,
          },
        },
      );
    },

    onMutate: async (vars) => {
      const clientSetId = pending.idFor(vars.rowKey);

      // Stop an in-flight refetch from landing on top of the patch below.
      await queryClient.cancelQueries({ queryKey: qk.session(sessionId) });

      const previous = queryClient.getQueryData<Session>(qk.session(sessionId));

      pending.setStatus(clientSetId, "inFlight");

      queryClient.setQueryData<Session>(qk.session(sessionId), (session) => {
        if (session === undefined) return session;
        // A retry re-enters onMutate with the same id; do not append a second copy.
        const alreadyThere = session.exercises.some((exercise) =>
          exercise.sets.some((set) => set.clientSetId === clientSetId),
        );
        if (alreadyThere) return session;

        return appendSet(session, vars.sessionExerciseId, {
          id: OPTIMISTIC_ID,
          order: Number.MAX_SAFE_INTEGER,
          kind: vars.kind,
          weight: vars.weight,
          unit: vars.unit,
          reps: vars.reps,
          rir: vars.rir,
          clientSetId,
          loggedAt: new Date().toISOString(),
        });
      });

      // Fired after the cache patch so the timer starts from the same tap, not from the response.
      options?.onLogged?.(vars);

      return { previous, clientSetId };
    },

    onSuccess: (set, vars, context) => {
      queryClient.setQueryData<Session>(qk.session(sessionId), (session) =>
        session === undefined
          ? session
          : upsertSet(session, vars.sessionExerciseId, context.clientSetId, set),
      );
      pending.clearStatus(context.clientSetId);
      // Released only now: until the server confirms, this row must keep its id.
      pending.release(vars.rowKey);
    },

    onError: (error, _vars, context) => {
      if (context === undefined) return;

      if (error.errorCode === "session.closed") {
        // The one case where an optimistic set is discarded. The session was closed elsewhere, so
        // the set can never be stored against it and a Retry would fail forever -- it is dropped
        // because retrying is impossible, not merely because it failed.
        queryClient.setQueryData<Session>(qk.session(sessionId), (session) =>
          session === undefined ? session : removeSet(session, context.clientSetId),
        );
        pending.clearStatus(context.clientSetId);
        void queryClient.invalidateQueries({ queryKey: qk.session(sessionId) });
        return;
      }

      // Everything else: keep the row, mark it unsynced, offer Retry. No rollback.
      pending.setStatus(context.clientSetId, "failed");
    },
  });
}

export function useStartSession(): UseMutationResult<
  Session,
  ApiError,
  { programId?: string; programDayId?: string; bodyweight?: number }
> {
  const api = useApi();
  const queryClient = useQueryClient();

  return useMutation<Session, ApiError, { programId?: string; programDayId?: string; bodyweight?: number }>({
    mutationFn: (vars) =>
      api.request<Session>("/sessions", {
        method: "POST",
        json: {
          programId: vars.programId ?? null,
          programDayId: vars.programDayId ?? null,
          bodyweight: vars.bodyweight ?? null,
        },
      }),
    onSuccess: (session) => {
      queryClient.setQueryData(qk.session(session.id), session);
      queryClient.setQueryData(qk.currentSession(), session);
    },
  });
}

export function useCompleteSession(sessionId: string): UseMutationResult<void, ApiError, void> {
  const api = useApi();
  const queryClient = useQueryClient();

  return useMutation<void, ApiError, void>({
    mutationFn: () => api.request<void>(`/sessions/${sessionId}/complete`, { method: "POST" }),
    onSuccess: async () => {
      queryClient.setQueryData(qk.currentSession(), null);
      await queryClient.invalidateQueries({ queryKey: qk.session(sessionId) });
      await queryClient.invalidateQueries({ queryKey: ["sessions"] });
    },
  });
}

export function useAbandonSession(sessionId: string): UseMutationResult<void, ApiError, void> {
  const api = useApi();
  const queryClient = useQueryClient();

  return useMutation<void, ApiError, void>({
    mutationFn: () => api.request<void>(`/sessions/${sessionId}/abandon`, { method: "POST" }),
    onSuccess: async () => {
      queryClient.setQueryData(qk.currentSession(), null);
      await queryClient.invalidateQueries({ queryKey: qk.session(sessionId) });
    },
  });
}

export function useUpdateProfile(): UseMutationResult<Profile, ApiError, UpdateProfileRequest> {
  const api = useApi();
  const queryClient = useQueryClient();

  return useMutation<Profile, ApiError, UpdateProfileRequest>({
    // PATCH /api/me is a full replace on this API, so every field must be sent; omitting one
    // clears it.
    mutationFn: (request) => api.request<Profile>("/me", { method: "PATCH", json: request }),
    onSuccess: (profile) => {
      queryClient.setQueryData(qk.profile(), profile);
    },
  });
}
