import { beforeEach, describe, expect, test, vi } from "vitest";
import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "../../test/msw/server";
import { IDS, completedSession } from "../../test/msw/fixtures";
import { createQueryClient } from "../api/queryClient";
import { AuthProvider } from "../auth/AuthProvider";
import { clearTokens } from "../auth/tokenStore";
import { TelegramProvider } from "../telegram/TelegramProvider";
import { createFakeRuntime, type FakeTelegramRuntime } from "../telegram/fake-runtime";
import { PendingSetIdsProvider } from "./PendingSetIdsProvider";
import { RestTimerProvider } from "./RestTimerProvider";
import { SessionScreen } from "./SessionScreen";

let runtime: FakeTelegramRuntime;
const onLeave = vi.fn();

beforeEach(() => {
  clearTokens();
  onLeave.mockClear();
  runtime = createFakeRuntime({ initData: "signed" });
});

function renderScreen(sessionId: string) {
  const client = createQueryClient();
  return render(
    <TelegramProvider runtime={runtime}>
      <QueryClientProvider client={client}>
        <AuthProvider>
          <RestTimerProvider>
            <PendingSetIdsProvider>
              <SessionScreen sessionId={sessionId} onLeave={onLeave} />
            </PendingSetIdsProvider>
          </RestTimerProvider>
        </AuthProvider>
      </QueryClientProvider>
    </TelegramProvider>,
  );
}

async function startSession(): Promise<string> {
  const response = await fetch("/api/sessions", {
    method: "POST",
    body: JSON.stringify({}),
    headers: { "content-type": "application/json" },
  });
  return ((await response.json()) as { id: string }).id;
}

const firstWorkingRow = () => screen.getByTestId(`row-${IDS.benchSessionExerciseId}:Working:0`);
const firstWarmupRow = () => screen.getByTestId(`row-${IDS.benchSessionExerciseId}:Warmup:0`);

describe("the session screen", () => {
  test("renders each exercise with its catalog name and target", async () => {
    const id = await startSession();
    renderScreen(id);

    expect(await screen.findByText("Bench press")).toBeInTheDocument();
    expect(screen.getByText("Incline DB press")).toBeInTheDocument();
    expect(screen.getByText(/target 3 × 8-10/)).toBeInTheDocument();
    expect(screen.getByText(/rest 2-3m/)).toBeInTheDocument();
  });

  test("pre-fills the first working row at the suggested weight and the bottom of the rep range", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    // Before any interaction: 80 kg for 8, which is the whole point of the pre-filled design.
    const row = firstWorkingRow();
    expect(within(row).getByRole("button", { name: /currently 80 kg/ })).toBeInTheDocument();
    expect(within(row).getByRole("button", { name: /currently 8$/ })).toBeInTheDocument();
  });

  test("renders warm-up weights 20, 40 and 62.5 for an 80 kg working weight", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    // The backend rounds DOWN to the plate increment. A round-to-nearest implementation would show
    // 22.5 / 42.5 / 65, so this is a regression guard on the rounding direction end to end.
    for (const weight of ["20kg", "40kg", "62.5kg"]) {
      expect(screen.getByRole("button", { name: new RegExp(`currently ${weight.replace("kg", " kg")}`) })).toBeInTheDocument();
    }
  });

  test("one tap on Log confirms the row", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    await userEvent.click(within(firstWorkingRow()).getByRole("button", { name: "Log" }));

    await waitFor(() => expect(within(firstWorkingRow()).queryByRole("button", { name: "Log" })).toBeNull());
    expect(within(firstWorkingRow()).getByText(/80kg × 8/)).toBeInTheDocument();
    expect(screen.getByText("1 / 6 working sets")).toBeInTheDocument();
  });

  test("logging a set starts the rest timer for that exercise's prescribed minimum", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    expect(screen.queryByText(/^Rest /)).toBeNull();
    await userEvent.click(within(firstWorkingRow()).getByRole("button", { name: "Log" }));

    // 120s is bench press's restMinSeconds; the countdown marks the earliest sensible next set.
    expect(await screen.findByText("Rest 2:00")).toBeInTheDocument();
    expect(screen.getByText("Bench press", { selector: ".rest-bar__exercise" })).toBeInTheDocument();
  });

  test("the rest timer is dismissable and never blocks logging", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    await userEvent.click(within(firstWorkingRow()).getByRole("button", { name: "Log" }));
    await screen.findByText("Rest 2:00");

    await userEvent.click(screen.getByRole("button", { name: "Dismiss rest timer" }));

    expect(screen.queryByText("Rest 2:00")).toBeNull();
    expect(
      within(screen.getByTestId(`row-${IDS.benchSessionExerciseId}:Working:1`)).getByRole("button", {
        name: "Log",
      }),
    ).toBeEnabled();
  });

  test("tapping the weight opens a stepper that steps by the profile's loading increment", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    const row = firstWorkingRow();
    await userEvent.click(within(row).getByRole("button", { name: /currently 80 kg/ }));

    // 2.5 kg, from the profile -- not a generic 1, which would make a barbell adjustment six taps.
    const increase = within(row).getByRole("button", { name: /Increase weight/ });
    await userEvent.click(increase);
    await userEvent.click(increase);

    expect(within(row).getByText("85")).toBeInTheDocument();
  });

  test("an adjusted weight is what gets logged", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    const row = firstWorkingRow();
    await userEvent.click(within(row).getByRole("button", { name: /currently 80 kg/ }));
    await userEvent.click(within(row).getByRole("button", { name: /Increase weight/ }));
    await userEvent.click(within(row).getByRole("button", { name: "Log" }));

    expect(await within(firstWorkingRow()).findByText(/82.5kg × 8/)).toBeInTheDocument();
  });

  test("warm-up rows offer no RIR control, because a warm-up has no reps in reserve", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    expect(within(firstWarmupRow()).queryByRole("button", { name: /reps in reserve/ })).toBeNull();
    expect(within(firstWorkingRow()).getByRole("button", { name: /reps in reserve/ })).toBeInTheDocument();
  });

  test("the expanded card is the first with unlogged sets", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    expect(screen.getByRole("button", { name: /Bench press/ })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: /Incline DB press/ })).toHaveAttribute("aria-expanded", "false");
  });

  test("tapping a collapsed card expands it instead", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    await userEvent.click(screen.getByRole("button", { name: /Incline DB press/ }));

    expect(screen.getByRole("button", { name: /Incline DB press/ })).toHaveAttribute("aria-expanded", "true");
  });

  test("+ add set appends a pending working row beyond the prescribed count", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    expect(screen.queryByTestId(`row-${IDS.benchSessionExerciseId}:Working:3`)).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "+ add set" }));

    // A fourth set is a normal thing to get; the UI should not be in the way.
    expect(screen.getByTestId(`row-${IDS.benchSessionExerciseId}:Working:3`)).toBeInTheDocument();
  });

  test("the Telegram main button reads Finish workout and completing leaves the screen", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    expect(runtime.mainButton.text).toBe("Finish workout");
    expect(runtime.mainButton.visible).toBe(true);

    await userEvent.click(within(firstWorkingRow()).getByRole("button", { name: "Log" }));
    await waitFor(() => expect(screen.getByText("1 / 6 working sets")).toBeInTheDocument());

    runtime.mainButton.click();

    await waitFor(() => expect(onLeave).toHaveBeenCalledTimes(1));
  });

  test("finishing with no working set shows the backend's reason and keeps the session open", async () => {
    const id = await startSession();
    renderScreen(id);
    await screen.findByText("Bench press");

    runtime.mainButton.click();

    expect(await screen.findByRole("alert")).toHaveTextContent("Log at least one working set before finishing.");
    expect(onLeave).not.toHaveBeenCalled();
  });

  test("closing confirmation is enabled for a live session and disabled on unmount", async () => {
    const id = await startSession();
    const view = renderScreen(id);
    await screen.findByText("Bench press");

    // Without this a swipe-down mid-workout closes the app silently.
    expect(runtime.closingConfirmationEnabled).toBe(true);

    view.unmount();
    expect(runtime.closingConfirmationEnabled).toBe(false);
  });

  test("a failed write keeps the row and offers Retry", async () => {
    const id = await startSession();
    server.use(http.post("/api/sessions/:id/exercises/:exerciseId/sets", () => HttpResponse.error()));
    renderScreen(id);
    await screen.findByText("Bench press");

    await userEvent.click(within(firstWorkingRow()).getByRole("button", { name: "Log" }));

    await waitFor(
      () => expect(within(firstWorkingRow()).getByRole("button", { name: "Retry" })).toBeInTheDocument(),
      { timeout: 15_000 },
    );
    expect(within(firstWorkingRow()).getByText("Not saved")).toBeInTheDocument();
  }, 30_000);

  test("a 404 shows the mapped message with a way back rather than a dead screen", async () => {
    renderScreen("00000000-0000-4000-8000-000000009999");

    expect(await screen.findByRole("alert")).toHaveTextContent("That workout is no longer there.");
    expect(screen.getByRole("button", { name: "Back to today" })).toBeInTheDocument();
  });
});

describe("a completed session", () => {
  test("renders read-only: no Log buttons, no steppers, no add set", async () => {
    renderScreen(completedSession.id);
    await screen.findByText("Bench press");

    expect(screen.queryByRole("button", { name: "Log" })).toBeNull();
    expect(screen.queryByRole("button", { name: "+ add set" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Adjust weight/ })).toBeNull();
    expect(screen.getByText("Completed")).toBeInTheDocument();
  });

  test("does not enable closing confirmation, since there is nothing to lose", async () => {
    renderScreen(completedSession.id);
    await screen.findByText("Bench press");

    expect(runtime.closingConfirmationEnabled).toBe(false);
  });

  test("hides the Finish workout main button", async () => {
    renderScreen(completedSession.id);
    await screen.findByText("Bench press");

    expect(runtime.mainButton.visible).toBe(false);
  });
});
