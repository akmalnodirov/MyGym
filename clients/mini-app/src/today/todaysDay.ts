import type { components } from "../api/generated";

type ProgramDay = components["schemas"]["ProgramDayResponse"];
type DayOfWeek = components["schemas"]["DayOfWeek"];

/**
 * `dayOfWeek` arrives as a name, not a number, so the mapping to `Date.getDay()` has to be an
 * explicit table.
 *
 * .NET's `DayOfWeek` happens to agree with JavaScript (both put Sunday at 0), which makes a cast
 * look tempting and correct. It is neither: the wire format is `"Monday"`, and getting Sunday
 * versus Monday wrong would show the athlete the wrong workout — a failure they would only notice
 * halfway through the wrong exercises.
 */
const WEEKDAY_INDEX: Record<Exclude<DayOfWeek, null>, number> = {
  Sunday: 0,
  Monday: 1,
  Tuesday: 2,
  Wednesday: 3,
  Thursday: 4,
  Friday: 5,
  Saturday: 6,
};

export function weekdayIndexOf(day: DayOfWeek): number | null {
  return day === null ? null : WEEKDAY_INDEX[day];
}

export interface TodaysDays {
  /** The day scheduled for `weekday`, or null when none is. */
  today: ProgramDay | null;
  /** Every other day, in program order. */
  rest: ProgramDay[];
}

/**
 * Splits a program's days into "the one for today" and the rest.
 *
 * `weekday` is `Date.getDay()` on the athlete's device. A program with no scheduled days — every
 * `dayOfWeek` null — simply promotes nothing and lists everything in order, which is the right
 * answer for someone who trains on whichever days they can.
 */
export function pickTodaysDay(days: ProgramDay[], weekday: number): TodaysDays {
  const ordered = [...days].sort((a, b) => a.order - b.order);

  // The first match, not the last: two days sharing a weekday is a program the athlete authored
  // oddly, and picking the earlier one at least matches their stated order.
  const today = ordered.find((day) => weekdayIndexOf(day.dayOfWeek) === weekday) ?? null;

  return {
    today,
    rest: today === null ? ordered : ordered.filter((day) => day.id !== today.id),
  };
}
