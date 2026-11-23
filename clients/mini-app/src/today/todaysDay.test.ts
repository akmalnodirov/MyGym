import { describe, expect, test } from "vitest";
import type { components } from "../api/generated";
import { pickTodaysDay, weekdayIndexOf } from "./todaysDay";

type ProgramDay = components["schemas"]["ProgramDayResponse"];

function day(over: Partial<ProgramDay> & { id: string }): ProgramDay {
  return {
    order: 0,
    label: "Day",
    dayOfWeek: null,
    prescriptions: [],
    ...over,
  };
}

describe("weekdayIndexOf", () => {
  test("maps every weekday name to the JS index, Sunday first", () => {
    // Asserted name by name rather than by a cast. The wire format is "Monday", and Sunday-versus-
    // Monday confusion would show the athlete the wrong workout -- a mistake they would only
    // notice halfway through the wrong exercises.
    expect(weekdayIndexOf("Sunday")).toBe(0);
    expect(weekdayIndexOf("Monday")).toBe(1);
    expect(weekdayIndexOf("Tuesday")).toBe(2);
    expect(weekdayIndexOf("Wednesday")).toBe(3);
    expect(weekdayIndexOf("Thursday")).toBe(4);
    expect(weekdayIndexOf("Friday")).toBe(5);
    expect(weekdayIndexOf("Saturday")).toBe(6);
  });

  test("an unscheduled day has no index", () => {
    expect(weekdayIndexOf(null)).toBeNull();
  });

  test("agrees with Date.getDay() for a known date", () => {
    // 2026-11-23 is a Monday.
    expect(new Date("2026-11-23T12:00:00Z").getUTCDay()).toBe(weekdayIndexOf("Monday"));
  });
});

describe("pickTodaysDay", () => {
  const push = day({ id: "push", order: 0, label: "Push day", dayOfWeek: "Monday" });
  const legs = day({ id: "legs", order: 1, label: "Legs", dayOfWeek: "Wednesday" });
  const full = day({ id: "full", order: 2, label: "Full body", dayOfWeek: "Friday" });

  test("promotes the matching day and keeps the rest in program order", () => {
    const { today, rest } = pickTodaysDay([push, legs, full], 3);

    expect(today?.id).toBe("legs");
    expect(rest.map((d) => d.id)).toEqual(["push", "full"]);
  });

  test("promotes nothing on a day with no scheduled workout, and lists everything", () => {
    const { today, rest } = pickTodaysDay([push, legs, full], 2);

    expect(today).toBeNull();
    expect(rest.map((d) => d.id)).toEqual(["push", "legs", "full"]);
  });

  test("promotes nothing when every day is unscheduled", () => {
    const unscheduled = [
      day({ id: "a", order: 0 }),
      day({ id: "b", order: 1 }),
    ];

    const { today, rest } = pickTodaysDay(unscheduled, 1);

    // A program with no weekdays is how someone who trains whenever they can authors it; that is
    // not an error state.
    expect(today).toBeNull();
    expect(rest.map((d) => d.id)).toEqual(["a", "b"]);
  });

  test("promotes only the first when two days share a weekday", () => {
    const second = day({ id: "push-again", order: 1, dayOfWeek: "Monday" });

    const { today, rest } = pickTodaysDay([push, second], 1);

    expect(today?.id).toBe("push");
    expect(rest.map((d) => d.id)).toEqual(["push-again"]);
  });

  test("sorts by order, not by the order the API happened to return", () => {
    const { rest } = pickTodaysDay([full, push, legs], 0);

    expect(rest.map((d) => d.id)).toEqual(["push", "legs", "full"]);
  });

  test("an empty program yields nothing rather than throwing", () => {
    const { today, rest } = pickTodaysDay([], 1);

    expect(today).toBeNull();
    expect(rest).toEqual([]);
  });
});
