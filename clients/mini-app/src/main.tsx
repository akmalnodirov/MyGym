import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { resolveRuntime } from "./telegram/fake-runtime";
import "./app.css";

async function start(): Promise<void> {
  // Mocks are opt-in and dynamically imported, so no handler, fixture or worker reaches the
  // production bundle. Run with VITE_USE_MSW=1 to serve the app from fixtures.
  if (import.meta.env.VITE_USE_MSW === "1") {
    const { worker } = await import("../test/msw/browser");
    await worker.start({ onUnhandledFrame: "warn" });
  }

  const root = document.getElementById("root");
  if (!root) throw new Error("#root is missing from index.html");

  let runtime;
  try {
    // Resolved before the first render so the app never has to handle a half-present runtime.
    runtime = await resolveRuntime();
  } catch (error) {
    // The one failure the app cannot route around: it is not running inside Telegram and no fake
    // was requested. Say so plainly rather than rendering a broken shell.
    root.textContent =
      error instanceof Error
        ? error.message
        : "This app has to run inside Telegram.";
    return;
  }

  createRoot(root).render(
    <StrictMode>
      <App runtime={runtime} />
    </StrictMode>,
  );
}

void start();
