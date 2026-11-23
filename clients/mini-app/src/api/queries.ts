import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import type { components } from "./generated";
import { useApi } from "../auth/AuthProvider";
import { useTelegram } from "../telegram/TelegramProvider";
import { qk } from "./queryKeys";

type Profile = components["schemas"]["ProfileResponse"];
type ExerciseSummary = components["schemas"]["ExerciseSummary"];
type Program = components["schemas"]["ProgramResponse"];
type ProgramSummary = components["schemas"]["ProgramSummaryResponse"];
type DayPreview = components["schemas"]["DayPreviewResponse"];
type Session = components["schemas"]["SessionResponse"];
type SessionSummary = components["schemas"]["SessionSummaryResponse"];

export function useProfile(): UseQueryResult<Profile> {
  const api = useApi();
  return useQuery({
    queryKey: qk.profile(),
    queryFn: () => api.request<Profile>("/me"),
    // The profile changes when the athlete changes it, which is rare and goes through a mutation
    // that invalidates this key anyway.
    staleTime: 5 * 60_000,
  });
}

export function useProgramSummaries(): UseQueryResult<ProgramSummary[]> {
  const api = useApi();
  return useQuery({
    queryKey: qk.programs(),
    queryFn: () => api.request<ProgramSummary[]>("/programs"),
  });
}

export function useProgram(id: string): UseQueryResult<Program> {
  const api = useApi();
  return useQuery({
    queryKey: qk.program(id),
    queryFn: () => api.request<Program>(`/programs/${id}`),
  });
}

export function useDayPreview(programId: string, dayId: string): UseQueryResult<DayPreview> {
  const api = useApi();
  return useQuery({
    queryKey: qk.dayPreview(programId, dayId),
    queryFn: () => api.request<DayPreview>(`/programs/${programId}/days/${dayId}/preview`),
  });
}

/**
 * Resolves to `null` when no workout is in progress.
 *
 * The API answers 204 in that case and the client turns an empty body into `null`, so "no
 * session" arrives as data rather than as an error the Today screen would have to interpret.
 */
export function useCurrentSession(): UseQueryResult<Session | null> {
  const api = useApi();
  return useQuery({
    queryKey: qk.currentSession(),
    queryFn: () => api.request<Session | null>("/sessions/current"),
    // Short, because resuming a workout is the one thing that must not be stale.
    staleTime: 5_000,
  });
}

export function useSession(id: string, options?: { enabled?: boolean }): UseQueryResult<Session> {
  const api = useApi();
  return useQuery({
    queryKey: qk.session(id),
    queryFn: () => api.request<Session>(`/sessions/${id}`),
    enabled: options?.enabled ?? true,
  });
}

export function useSessionSummaries(page: number): UseQueryResult<SessionSummary[]> {
  const api = useApi();
  return useQuery({
    queryKey: qk.sessions(page),
    queryFn: () => api.request<SessionSummary[]>(`/sessions?page=${page}&pageSize=20`),
  });
}

/**
 * The exercise catalog, as an id-to-name lookup.
 *
 * Neither the day preview nor the session response carries an exercise name — only an id — so the
 * client has to hold the catalog. It is seeded, shared between all users and effectively
 * immutable, hence `staleTime: Infinity`: one fetch per launch, reused by every screen.
 */
export function useExerciseNames(): {
  nameFor: (id: string) => string;
  isLoading: boolean;
} {
  const api = useApi();
  const telegram = useTelegram();
  const lang = telegram.languageCode ?? "en";

  const query = useQuery({
    queryKey: qk.exercises(lang),
    queryFn: () => api.request<ExerciseSummary[]>(`/exercises?lang=${encodeURIComponent(lang)}`),
    staleTime: Number.POSITIVE_INFINITY,
  });

  const byId = new Map((query.data ?? []).map((exercise) => [exercise.id, exercise.name]));

  return {
    // A missing name is cosmetic; a thrown error here would take down the whole workout screen.
    nameFor: (id: string) => byId.get(id) ?? "Unknown exercise",
    isLoading: query.isLoading,
  };
}
