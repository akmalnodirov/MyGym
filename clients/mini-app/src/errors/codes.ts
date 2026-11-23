/**
 * The backend's stable error codes, mapped to what the athlete reads and what the app does next.
 *
 * Codes come from the API's ProblemDetails `errorCode` extension. They are the contract; the
 * `title` alongside them is a human message that may be reworded, and HTTP status alone is
 * ambiguous — 409 by itself covers nine of these.
 *
 * An unmapped code must degrade to a usable screen. The backend will gain codes this client has
 * never seen, and a missing table entry is not a reason to show a crash.
 */

export type RecoveryAction =
  /** Show the message; the athlete decides what to do. */
  | "none"
  /** A workout is already open — go to it instead of reporting a failure. */
  | "resumeCurrentSession"
  /** This session was closed elsewhere; reload it and fall back to read-only. */
  | "refetchReadOnly"
  /** The addressed thing is gone; return to Today rather than leaving a dead screen. */
  | "goHome"
  /** Exchange initData again. */
  | "reauthenticate";

interface CodeEntry {
  message: string;
  recovery: RecoveryAction;
}

const GENERIC_MESSAGE = "Something went wrong. Try again.";

const CODES: Record<string, CodeEntry> = {
  // ── Session state ───────────────────────────────────────────────────────
  "session.already_in_progress": {
    message: "You already have a workout open.",
    recovery: "resumeCurrentSession",
  },
  "session.closed": {
    message: "This workout was already finished.",
    recovery: "refetchReadOnly",
  },
  "session.needs_at_least_one_working_set": {
    message: "Log at least one working set before finishing.",
    recovery: "none",
  },
  "session.needs_at_least_one_exercise": {
    message: "This workout has no exercises yet.",
    recovery: "none",
  },

  // ── Gone ────────────────────────────────────────────────────────────────
  "session.not_found": { message: "That workout is no longer there.", recovery: "goHome" },
  "session.exercise_not_found": { message: "That exercise is no longer in this workout.", recovery: "goHome" },
  "set.not_found": { message: "That set is no longer there.", recovery: "goHome" },
  "program.not_found": { message: "That program is no longer there.", recovery: "goHome" },
  "program.day_not_found": { message: "That training day is no longer there.", recovery: "goHome" },
  "exercise.not_found": { message: "That exercise is not in the catalog.", recovery: "goHome" },

  // ── Authentication ──────────────────────────────────────────────────────
  "auth.refresh_token_invalid": { message: "Signing you in again.", recovery: "reauthenticate" },
  "auth.init_data_expired": { message: "Signing you in again.", recovery: "reauthenticate" },
  "auth.init_data_signature_invalid": {
    message: "Telegram could not verify this session. Close and reopen the app.",
    recovery: "reauthenticate",
  },
  "auth.init_data_malformed": {
    message: "Telegram sent something this app could not read. Close and reopen it.",
    recovery: "none",
  },

  // ── Set values ──────────────────────────────────────────────────────────
  "set.reps_out_of_bounds": { message: "That rep count is out of range.", recovery: "none" },
  "set.rir_out_of_bounds": { message: "That RIR value is out of range.", recovery: "none" },
  "set.kind_invalid": { message: "That set type is not recognised.", recovery: "none" },

  // ── Program state ───────────────────────────────────────────────────────
  "program.already_active": { message: "That program is already your active one.", recovery: "none" },
  "program.archived": { message: "That program is archived.", recovery: "none" },
  "program.not_editable": { message: "That program cannot be edited.", recovery: "none" },
  "program.edit_conflict": {
    message: "Someone changed this program while you were editing. Reload and try again.",
    recovery: "none",
  },
  "program.needs_at_least_one_day": { message: "A program needs at least one training day.", recovery: "none" },
  "program.day_needs_at_least_one_prescription": {
    message: "A training day needs at least one exercise.",
    recovery: "none",
  },

  // ── Profile ─────────────────────────────────────────────────────────────
  "profile.time_zone_unknown": { message: "That time zone is not recognised.", recovery: "none" },
  "profile.weight_unit_invalid": { message: "Choose either kilograms or pounds.", recovery: "none" },
  "profile.birth_year_out_of_bounds": { message: "That birth year is out of range.", recovery: "none" },
  "profile.available_days_out_of_bounds": { message: "Training days must be between 1 and 7.", recovery: "none" },
  "profile.session_minutes_out_of_bounds": { message: "That session length is out of range.", recovery: "none" },
  "profile.experience_level_invalid": { message: "Choose an experience level from the list.", recovery: "none" },
};

export function messageForCode(code: string | null): string {
  if (code === null || code.length === 0) return GENERIC_MESSAGE;
  return CODES[code]?.message ?? GENERIC_MESSAGE;
}

export function recoveryForCode(code: string | null): RecoveryAction {
  if (code === null || code.length === 0) return "none";
  return CODES[code]?.recovery ?? "none";
}
