import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { TelegramProvider } from "../telegram/TelegramProvider";
import { createFakeRuntime, type FakeTelegramRuntime } from "../telegram/fake-runtime";
import { RestTimerProvider, formatRemaining, useRestTimer } from "./RestTimerProvider";

function Display() {
  const { remainingMs, label, start, dismiss } = useRestTimer();
  return (
    <div>
      <output data-testid="remaining">{remainingMs === null ? "idle" : String(remainingMs)}</output>
      <output data-testid="label">{label ?? "none"}</output>
      <button type="button" onClick={() => start(180, "Bench press")}>
        start 180
      </button>
      <button type="button" onClick={() => start(90, "Incline DB press")}>
        start 90
      </button>
      <button type="button" onClick={dismiss}>
        dismiss
      </button>
    </div>
  );
}

function harness(runtime: FakeTelegramRuntime) {
  return (
    <TelegramProvider runtime={runtime}>
      <RestTimerProvider>
        <Display />
      </RestTimerProvider>
    </TelegramProvider>
  );
}

/** Clicks without userEvent, which does not cooperate with fake timers. */
function click(name: string): void {
  act(() => {
    screen.getByRole("button", { name }).click();
  });
}

function advance(ms: number): void {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
}

describe("the rest timer", () => {
  let runtime: FakeTelegramRuntime;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-23T10:00:00Z"));
    runtime = createFakeRuntime();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  test("is idle until started", () => {
    render(harness(runtime));

    expect(screen.getByTestId("remaining")).toHaveTextContent("idle");
    expect(screen.getByTestId("label")).toHaveTextContent("none");
  });

  test("counts down from the given duration", () => {
    render(harness(runtime));
    click("start 180");

    expect(screen.getByTestId("remaining")).toHaveTextContent("180000");
    expect(screen.getByTestId("label")).toHaveTextContent("Bench press");

    advance(30_000);
    expect(screen.getByTestId("remaining")).toHaveTextContent("150000");
  });

  test("is correct after a backgrounded gap, because it stores a target timestamp", () => {
    render(harness(runtime));
    click("start 180");

    // The webview suspended: the wall clock jumped 170s forward while no interval fired, leaving
    // 10s. One 250ms tick is then needed to re-read the clock, and that tick advances it too, so
    // 9750 is the expected value.
    //
    // The number that matters is what it is NOT: a decrementing counter would still read 180000
    // here, because its interval never ran. That is the whole reason this stores a target
    // timestamp.
    act(() => {
      vi.setSystemTime(new Date("2026-11-23T10:02:50Z"));
      vi.advanceTimersByTime(250);
    });

    expect(screen.getByTestId("remaining")).toHaveTextContent("9750");
  });

  test("does not go negative when the gap outlasts the countdown", () => {
    render(harness(runtime));
    click("start 180");

    act(() => {
      vi.setSystemTime(new Date("2026-11-23T10:30:00Z"));
      vi.advanceTimersByTime(250);
    });

    expect(screen.getByTestId("remaining")).toHaveTextContent("0");
  });

  test("fires the success haptic exactly once at zero", () => {
    render(harness(runtime));
    click("start 180");
    expect(runtime.successNotifications).toBe(0);

    advance(180_000);
    expect(runtime.successNotifications).toBe(1);

    // Ticks keep arriving while the timer sits at zero; the haptic must not repeat.
    advance(5_000);
    expect(runtime.successNotifications).toBe(1);
  });

  test("fires the haptic once even when the countdown elapsed entirely in the background", () => {
    render(harness(runtime));
    click("start 180");

    act(() => {
      vi.setSystemTime(new Date("2026-11-23T10:10:00Z"));
      vi.advanceTimersByTime(250);
    });

    expect(runtime.successNotifications).toBe(1);
  });

  test("dismiss clears it immediately", () => {
    render(harness(runtime));
    click("start 180");
    advance(10_000);

    click("dismiss");

    expect(screen.getByTestId("remaining")).toHaveTextContent("idle");
    expect(screen.getByTestId("label")).toHaveTextContent("none");
  });

  test("starting a new timer replaces the running one rather than stacking", () => {
    render(harness(runtime));
    click("start 180");
    advance(60_000);

    click("start 90");

    expect(screen.getByTestId("remaining")).toHaveTextContent("90000");
    expect(screen.getByTestId("label")).toHaveTextContent("Incline DB press");
  });

  test("a replacement timer gets its own haptic", () => {
    render(harness(runtime));
    click("start 180");
    advance(180_000);
    expect(runtime.successNotifications).toBe(1);

    click("start 90");
    advance(90_000);

    expect(runtime.successNotifications).toBe(2);
  });

  test("stops ticking once dismissed, so nothing runs in the background forever", () => {
    render(harness(runtime));
    click("start 180");
    click("dismiss");

    expect(vi.getTimerCount()).toBe(0);
  });

  test("useRestTimer outside its provider is a clear error", () => {
    function Orphan() {
      useRestTimer();
      return null;
    }

    expect(() => render(<Orphan />)).toThrow(/RestTimerProvider/);
  });
});

describe("formatRemaining", () => {
  test("renders minutes and zero-padded seconds", () => {
    expect(formatRemaining(180_000)).toBe("3:00");
    expect(formatRemaining(125_000)).toBe("2:05");
    expect(formatRemaining(59_000)).toBe("0:59");
    expect(formatRemaining(0)).toBe("0:00");
  });

  test("rounds up, so a timer never shows 0:00 while time remains", () => {
    // 1 ms left is still "1 second" to a reader; showing 0:00 early would look like it finished.
    expect(formatRemaining(1)).toBe("0:01");
    expect(formatRemaining(1_001)).toBe("0:02");
  });
});
