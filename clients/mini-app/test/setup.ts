import "@testing-library/jest-dom/vitest";
import { afterAll, afterEach, beforeAll, beforeEach } from "vitest";
import { cleanup } from "@testing-library/react";
import { server } from "./msw/server";
import { resetFixtureState } from "./msw/fixtures";
import { resetProfile } from "./msw/handlers";

// "error" is deliberate: a request the handlers do not cover must fail the test loudly rather than
// escape to the network, where it would either hang or quietly succeed against something real.
// MSW 3 renamed this from onUnhandledRequest to onUnhandledFrame, since it now also covers
// WebSocket connections.
beforeAll(() => {
  server.listen({ onUnhandledFrame: "error" });
});

beforeEach(() => {
  resetFixtureState();
  resetProfile();
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
});

afterAll(() => {
  server.close();
});
