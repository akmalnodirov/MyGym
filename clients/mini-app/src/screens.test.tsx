import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import type { ReactNode } from "react";
import { server } from "../test/msw/server";
import { IDS, authTokens, profile } from "../test/msw/fixtures";
import { createQueryClient } from "./api/queryClient";
import { AuthProvider } from "./auth/AuthProvider";
import { clearTokens } from "./auth/tokenStore";
import { TelegramProvider } from "./telegram/TelegramProvider";
import { createFakeRuntime, type FakeTelegramRuntime } from "./telegram/fake-runtime";
import { PendingSetIdsProvider } from "./session/PendingSetIdsProvider";
import { RestTimerProvider } from "./session/RestTimerProvider";
import { TodayScreen } from "./today/TodayScreen";
import { DayPreviewScreen } from "./preview/DayPreviewScreen";
import { HistoryScreen } from "./history/HistoryScreen";
import { ProfileScreen } from "./profile/ProfileScreen";

let runtime: FakeTelegramRuntime;

beforeEach(() => {
  clearTokens();
  runtime = createFakeRuntime({ initData: "signed" });
});

function mount(children: ReactNode) {
  return render(
    <TelegramProvider runtime={runtime}>
      <QueryClientProvider client={createQueryClient()}>
        <AuthProvider>
          <RestTimerProvider>
            <PendingSetIdsProvider>{children}</PendingSetIdsProvider>
          </RestTimerProvider>
        </AuthProvider>
      </QueryClientProvider>
    </TelegramProvider>,
  );
}

async function startSession(): Promise<void> {
  await fetch("/api/sessions", {
    method: "POST",
    body: JSON.stringify({}),
    headers: { "content-type": "application/json" },
  });
}

const noop = () => undefined;

describe("the Today screen", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  test("lists the active program's days and labels today's", async () => {
    // The fixture's Push day is scheduled for Monday; 2026-11-23 is a Monday.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date("2026-11-23T09:00:00"));

    mount(<TodayScreen onOpenDay={noop} onResume={noop} onOpenHistory={noop} onOpenProfile={noop} />);

    expect(await screen.findByText("Push day")).toBeInTheDocument();
    expect(screen.getByText("Today")).toBeInTheDocument();
  });

  test("does not label a day Today when nothing is scheduled for it", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    // A Tuesday.
    vi.setSystemTime(new Date("2026-11-24T09:00:00"));

    mount(<TodayScreen onOpenDay={noop} onResume={noop} onOpenHistory={noop} onOpenProfile={noop} />);

    await screen.findByText("Push day");
    expect(screen.queryByText("Today")).toBeNull();
  });

  test("leads with Resume workout when a session is open", async () => {
    await startSession();

    mount(<TodayScreen onOpenDay={noop} onResume={noop} onOpenHistory={noop} onOpenProfile={noop} />);

    expect(await screen.findByText("Resume workout")).toBeInTheDocument();
    expect(screen.getByText("in progress")).toBeInTheDocument();
  });

  test("does not show Resume when the API answers 204", async () => {
    mount(<TodayScreen onOpenDay={noop} onResume={noop} onOpenHistory={noop} onOpenProfile={noop} />);

    await screen.findByText("Push day");
    // 204 means no workout in progress -- a normal state, not an error and not an empty session.
    expect(screen.queryByText("Resume workout")).toBeNull();
  });

  test("resuming reports the open session's id", async () => {
    await startSession();
    const onResume = vi.fn();

    mount(<TodayScreen onOpenDay={noop} onResume={onResume} onOpenHistory={noop} onOpenProfile={noop} />);
    await userEvent.click(await screen.findByText("Resume workout"));

    expect(onResume).toHaveBeenCalledWith(IDS.sessionId);
  });

  test("tapping a day reports the program and day ids", async () => {
    const onOpenDay = vi.fn();

    mount(<TodayScreen onOpenDay={onOpenDay} onResume={noop} onOpenHistory={noop} onOpenProfile={noop} />);
    await userEvent.click(await screen.findByText("Push day"));

    expect(onOpenDay).toHaveBeenCalledWith(IDS.programId, IDS.dayId);
  });

  test("shows guidance when there is no active program", async () => {
    server.use(http.get("/api/programs", () => HttpResponse.json([])));

    mount(<TodayScreen onOpenDay={noop} onResume={noop} onOpenHistory={noop} onOpenProfile={noop} />);

    expect(await screen.findByText("No active program")).toBeInTheDocument();
  });
});

describe("the Day preview screen", () => {
  const props = {
    programId: IDS.programId,
    dayId: IDS.dayId,
    onStarted: noop,
    onResumeCurrent: noop,
    onLeave: noop,
  };

  test("shows each exercise with its name, target and resolved warm-up weights", async () => {
    mount(<DayPreviewScreen {...props} />);

    expect(await screen.findByText("Push day")).toBeInTheDocument();
    expect(screen.getByText("Bench press")).toBeInTheDocument();
    // The backend's round-down values, carried through the preview unchanged.
    expect(screen.getByText(/20kg×10\s+40kg×5\s+62\.5kg×3\?/)).toBeInTheDocument();
    expect(screen.getByText(/3 × 8-10 @ 80kg/)).toBeInTheDocument();
  });

  test("the main button starts the session and reports its id", async () => {
    const onStarted = vi.fn();
    mount(<DayPreviewScreen {...props} onStarted={onStarted} />);
    await screen.findByText("Push day");

    expect(runtime.mainButton.text).toBe("Start workout");
    runtime.mainButton.click();

    await waitFor(() => expect(onStarted).toHaveBeenCalledWith(IDS.sessionId));
  });

  test("a 409 session.already_in_progress sends the athlete to the open workout, not an error", async () => {
    await startSession();
    const onResumeCurrent = vi.fn();
    const onStarted = vi.fn();

    mount(<DayPreviewScreen {...props} onStarted={onStarted} onResumeCurrent={onResumeCurrent} />);
    await screen.findByText("Push day");
    runtime.mainButton.click();

    // They wanted to be in a workout; taking them to the one they have beats explaining a
    // conflict.
    await waitFor(() => expect(onResumeCurrent).toHaveBeenCalledTimes(1));
    expect(onStarted).not.toHaveBeenCalled();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  test("shows a dash rather than null when there is no suggested weight yet", async () => {
    server.use(
      http.get("/api/programs/:id/days/:dayId/preview", () =>
        HttpResponse.json({
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
              suggestedWorkingWeight: null,
              weightUnit: "Kilograms",
              warmup: [],
              notes: null,
            },
          ],
        }),
      ),
    );

    mount(<DayPreviewScreen {...props} />);

    expect(await screen.findByText(/3 × 8-10 @ —/)).toBeInTheDocument();
    expect(screen.queryByText(/null|NaN/)).toBeNull();
  });

  test("a missing day shows the mapped message with a way back", async () => {
    mount(<DayPreviewScreen {...props} dayId="00000000-0000-4000-8000-000000009999" />);

    expect(await screen.findByRole("alert")).toHaveTextContent("That training day is no longer there.");
    expect(screen.getByRole("button", { name: "Back to today" })).toBeInTheDocument();
  });
});

describe("the History screen", () => {
  test("lists past sessions with their date and working-set count", async () => {
    mount(<HistoryScreen onOpenSession={noop} />);

    expect(await screen.findByText(/6 working sets/)).toBeInTheDocument();
  });

  test("tapping a session reports its id", async () => {
    const onOpenSession = vi.fn();
    mount(<HistoryScreen onOpenSession={onOpenSession} />);

    await userEvent.click(await screen.findByText(/6 working sets/));

    expect(onOpenSession).toHaveBeenCalledWith(IDS.completedSessionId);
  });

  test("shows an empty state before the first workout", async () => {
    server.use(http.get("/api/sessions", () => HttpResponse.json([])));

    mount(<HistoryScreen onOpenSession={noop} />);

    expect(await screen.findByText("No workouts yet")).toBeInTheDocument();
  });

  test("offers Load more only when a full page came back, and appends the next one", async () => {
    const page = (n: number) =>
      Array.from({ length: 20 }, (_, i) => ({
        id: `00000000-0000-4000-8000-${String(n * 100 + i).padStart(12, "0")}`,
        status: "Completed" as const,
        startedAt: `2026-1${n}-0${(i % 9) + 1}T09:00:00+00:00`,
        completedAt: `2026-1${n}-0${(i % 9) + 1}T10:00:00+00:00`,
        workingSetCount: n * 100 + i,
      }));

    server.use(
      http.get("/api/sessions", ({ request }) => {
        const requested = Number(new URL(request.url).searchParams.get("page") ?? "1");
        return HttpResponse.json(requested === 1 ? page(1) : page(2).slice(0, 5));
      }),
    );

    mount(<HistoryScreen onOpenSession={noop} />);
    await screen.findByText(/100 working sets/);

    await userEvent.click(screen.getByRole("button", { name: "Load more" }));

    // Appended, not replaced: page one is still on screen alongside page two.
    expect(await screen.findByText(/200 working sets/)).toBeInTheDocument();
    expect(screen.getByText(/100 working sets/)).toBeInTheDocument();
    // Five rows is a partial page, so there is nothing more to load.
    await waitFor(() => expect(screen.queryByRole("button", { name: "Load more" })).toBeNull());
  });
});

describe("the Profile screen", () => {
  test("shows the current unit, increment and bodyweight", async () => {
    mount(<ProfileScreen onSaved={noop} />);

    expect(await screen.findByDisplayValue("Kilograms")).toBeInTheDocument();
    expect(screen.getByDisplayValue("2.5")).toBeInTheDocument();
    expect(screen.getByDisplayValue("74")).toBeInTheDocument();
  });

  test("saving sends every field, because PATCH /api/me is a full replace", async () => {
    let body: Record<string, unknown> | null = null;
    server.use(
      http.patch("/api/me", async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(profile);
      }),
    );
    const onSaved = vi.fn();

    mount(<ProfileScreen onSaved={onSaved} />);
    await screen.findByDisplayValue("Kilograms");
    runtime.mainButton.click();

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    // Omitting a field would clear it server-side, so all eight go every time.
    expect(Object.keys(body ?? {}).sort()).toEqual([
      "availableDaysPerWeek",
      "birthYear",
      "bodyweight",
      "experienceLevel",
      "loadingIncrement",
      "preferredUnit",
      "sessionMinutes",
      "timeZoneId",
    ]);
  });

  test("changing the unit is sent as the new unit", async () => {
    let body: { preferredUnit?: string } | null = null;
    server.use(
      http.patch("/api/me", async ({ request }) => {
        body = (await request.json()) as { preferredUnit?: string };
        return HttpResponse.json({ ...profile, preferredUnit: "Pounds" });
      }),
    );

    mount(<ProfileScreen onSaved={noop} />);
    await screen.findByDisplayValue("Kilograms");

    await userEvent.selectOptions(screen.getByRole("combobox", { name: /Weight unit/ }), "Pounds");
    runtime.mainButton.click();

    await waitFor(() => expect(body?.preferredUnit).toBe("Pounds"));
  });

  test("a rejected time zone is shown against the field, not only as a banner", async () => {
    mount(<ProfileScreen onSaved={noop} />);
    const field = await screen.findByDisplayValue("Asia/Tashkent");

    await userEvent.clear(field);
    await userEvent.type(field, "Mars/Olympus_Mons");
    runtime.mainButton.click();

    const message = "That time zone is not recognised.";
    await waitFor(() => expect(screen.getAllByText(message).length).toBeGreaterThan(0));
    expect(document.querySelector(".field__error")).toHaveTextContent(message);
  });
});

describe("authentication wiring", () => {
  test("every screen above renders only after the initData exchange succeeds", async () => {
    let exchanges = 0;
    server.use(
      http.post("/api/auth/telegram", () => {
        exchanges += 1;
        return HttpResponse.json(authTokens);
      }),
    );

    mount(<HistoryScreen onOpenSession={noop} />);
    await screen.findByText("History");

    expect(exchanges).toBe(1);
  });
});
