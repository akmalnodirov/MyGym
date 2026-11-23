import { beforeEach, describe, expect, test, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "../test/msw/server";
import { IDS } from "../test/msw/fixtures";
import { clearTokens } from "./auth/tokenStore";
import { createFakeRuntime, type FakeTelegramRuntime } from "./telegram/fake-runtime";
import { App } from "./App";

let runtime: FakeTelegramRuntime;

beforeEach(() => {
  clearTokens();
  runtime = createFakeRuntime({ initData: "signed" });
});

function launch(at = "/") {
  return render(<App runtime={runtime} initialEntries={[at]} />);
}

describe("routing", () => {
  test("the root route renders Today", async () => {
    launch();

    expect(await screen.findByText("MyGym")).toBeInTheDocument();
    expect(await screen.findByText("Push day")).toBeInTheDocument();
  });

  test("each route renders its screen", async () => {
    launch("/history");
    expect(await screen.findByText("History")).toBeInTheDocument();

    launch("/profile");
    expect(await screen.findByText("Profile")).toBeInTheDocument();

    launch(`/programs/${IDS.programId}/days/${IDS.dayId}`);
    expect(await screen.findByText("Push day")).toBeInTheDocument();
  });

  test("an unknown path lands on Today rather than on nothing", async () => {
    // Telegram can open a Mini App on a start-parameter path the client does not serve.
    launch("/some/path/we/never/defined");

    expect(await screen.findByText("MyGym")).toBeInTheDocument();
  });

  test("expand() is called once at launch", async () => {
    launch();
    await screen.findByText("MyGym");

    expect(runtime.expandCount).toBe(1);
  });

  test("the theme is published as CSS custom properties", async () => {
    launch();
    await screen.findByText("MyGym");

    expect(document.documentElement.style.getPropertyValue("--tg-bg-color")).toBe("#ffffff");
  });
});

describe("the Telegram back button", () => {
  test("is hidden at the root", async () => {
    launch();
    await screen.findByText("MyGym");

    expect(runtime.backButton.visible).toBe(false);
  });

  test("is shown after navigating to a child route, and navigates back", async () => {
    launch();
    await userEvent.click(await screen.findByText("History"));
    await screen.findByText(/working set/);

    expect(runtime.backButton.visible).toBe(true);

    runtime.backButton.click();

    expect(await screen.findByText("Push day")).toBeInTheDocument();
    await waitFor(() => expect(runtime.backButton.visible).toBe(false));
  });
});

describe("the error boundary", () => {
  test("a screen that throws renders a reload offer instead of a blank app", async () => {
    // A 200 with a body the screen cannot use is the realistic shape of this failure: the session
    // screen reads data.exercises, and null is not iterable.
    server.use(
      http.get("/api/sessions/:id", () =>
        HttpResponse.json({
          id: IDS.sessionId,
          status: "InProgress",
          startedAt: "2026-11-23T09:00:00+00:00",
          completedAt: null,
          programId: null,
          programRevision: null,
          programDayId: null,
          bodyweightAtSession: null,
          workingSetCount: 0,
          exercises: null,
        }),
      ),
    );
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);

    launch(`/sessions/${IDS.sessionId}`);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Something broke on this screen.");
    expect(within(alert).getByRole("button", { name: "Reload" })).toBeInTheDocument();
    // The reassurance matters: the sets really are on the server already.
    expect(alert).toHaveTextContent("Your logged sets are saved.");

    consoleError.mockRestore();
  });
});

describe("the critical path", () => {
  test("sign in, pick today, start, log a working set, finish", async () => {
    // The whole product in one test: everything the athlete does three times a week.
    launch();

    // Today, with Monday's day listed.
    await screen.findByText("Push day");

    // The preview — the screen that replaces the text file.
    await userEvent.click(screen.getByText("Push day"));
    await screen.findByText("Bench press");
    expect(screen.getByText(/20kg×10\s+40kg×5\s+62\.5kg×3\?/)).toBeInTheDocument();

    // Start, via Telegram's own main button.
    expect(runtime.mainButton.text).toBe("Start workout");
    runtime.mainButton.click();

    // The session screen, pre-filled.
    await waitFor(() => expect(screen.getByText("0 / 6 working sets")).toBeInTheDocument());
    const row = screen.getByTestId(`row-${IDS.benchSessionExerciseId}:Working:0`);
    expect(within(row).getByRole("button", { name: /currently 80 kg/ })).toBeInTheDocument();

    // One tap logs it, and the rest timer starts itself.
    await userEvent.click(within(row).getByRole("button", { name: "Log" }));
    await waitFor(() => expect(screen.getByText("1 / 6 working sets")).toBeInTheDocument());
    expect(screen.getByText("Rest 2:00")).toBeInTheDocument();

    // Finish, again via the main button.
    await waitFor(() => expect(runtime.mainButton.text).toBe("Finish workout"));
    runtime.mainButton.click();

    // Back on Today, with no workout open any more.
    await waitFor(() => expect(screen.getByText("MyGym")).toBeInTheDocument());
    await waitFor(() => expect(screen.queryByText("Resume workout")).toBeNull());
  }, 30_000);

  test("a workout left open is offered for resuming on the next launch", async () => {
    await fetch("/api/sessions", {
      method: "POST",
      body: JSON.stringify({}),
      headers: { "content-type": "application/json" },
    });

    launch();

    expect(await screen.findByText("Resume workout")).toBeInTheDocument();
    await userEvent.click(screen.getByText("Resume workout"));

    expect(await screen.findByText("0 / 6 working sets")).toBeInTheDocument();
  });
});
