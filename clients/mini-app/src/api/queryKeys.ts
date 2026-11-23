/**
 * Every query key in one place, so the optimistic mutation and the screens cannot disagree about
 * which cache entry they are reading and writing.
 *
 * `currentSession` is deliberately separate from `session(id)`. GET /api/sessions/current answers
 * 204 when nothing is open, which makes it a distinct resource — "is there a workout?" — rather
 * than the same session under another name.
 */
export const qk = {
  profile: () => ["profile"] as const,
  exercises: (lang: string) => ["exercises", lang] as const,
  programs: () => ["programs"] as const,
  program: (id: string) => ["program", id] as const,
  dayPreview: (programId: string, dayId: string) => ["dayPreview", programId, dayId] as const,
  currentSession: () => ["session", "current"] as const,
  session: (id: string) => ["session", id] as const,
  sessions: (page: number) => ["sessions", page] as const,
};
