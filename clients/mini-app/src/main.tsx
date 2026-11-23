import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

async function start(): Promise<void> {
  // Mocks are opt-in and dynamically imported, so no handler, fixture or worker reaches the
  // production bundle. Run with VITE_USE_MSW=1 to serve the app from fixtures.
  if (import.meta.env.VITE_USE_MSW === "1") {
    const { worker } = await import("../test/msw/browser");
    await worker.start({ onUnhandledFrame: "warn" });
  }

  const root = document.getElementById("root");
  if (!root) throw new Error("#root is missing from index.html");

  createRoot(root).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

void start();
