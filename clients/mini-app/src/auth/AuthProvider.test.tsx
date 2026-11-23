import { beforeEach, expect, test, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { useEffect } from "react";
import { server } from "../../test/msw/server";
import { authTokens } from "../../test/msw/fixtures";
import { TelegramProvider } from "../telegram/TelegramProvider";
import { createFakeRuntime } from "../telegram/fake-runtime";
import { AuthProvider, useApi } from "./AuthProvider";
import { clearTokens, getAccessToken } from "./tokenStore";

beforeEach(() => {
  clearTokens();
});

function signedIn(initData = "signed-init-data", children?: React.ReactNode) {
  const runtime = createFakeRuntime({ initData });
  return (
    <TelegramProvider runtime={runtime}>
      <AuthProvider>{children ?? <div>Signed in</div>}</AuthProvider>
    </TelegramProvider>
  );
}

/** Issues one GET /api/me and reports what came back, so a 401 path can be observed. */
function Probe({ onResult }: { onResult: (value: string) => void }) {
  const api = useApi();
  useEffect(() => {
    void api
      .request<{ firstName: string }>("/me")
      .then((profile) => onResult(profile.firstName))
      .catch((error: unknown) => onResult(`error:${String(error)}`));
  }, [api, onResult]);
  return null;
}

test("exchanges initData on mount and renders children once signed in", async () => {
  render(signedIn());

  expect(await screen.findByText("Signed in")).toBeInTheDocument();
});

test("writes no token to localStorage or sessionStorage", async () => {
  render(signedIn());
  await screen.findByText("Signed in");

  // The design decision, enforced: a persisted refresh token is an exfiltration target bought for
  // nothing, because initData is available on every launch.
  expect(localStorage.length).toBe(0);
  expect(sessionStorage.length).toBe(0);
  expect(document.cookie).toBe("");
  expect(getAccessToken()).toBe(authTokens.accessToken);
});

test("renders an error with a retry action when the exchange fails, and never renders children", async () => {
  render(signedIn("invalid"));

  expect(await screen.findByRole("alert")).toBeInTheDocument();
  expect(screen.queryByText("Signed in")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
});

test("retrying after a failure signs in when the next attempt succeeds", async () => {
  let attempts = 0;
  server.use(
    http.post("/api/auth/telegram", () => {
      attempts += 1;
      if (attempts === 1) {
        return HttpResponse.json(
          { title: "nope", status: 401, errorCode: "auth.init_data_signature_invalid" },
          { status: 401 },
        );
      }
      return HttpResponse.json(authTokens);
    }),
  );

  render(signedIn());
  await screen.findByRole("alert");

  await userEvent.click(screen.getByRole("button", { name: "Try again" }));

  expect(await screen.findByText("Signed in")).toBeInTheDocument();
  expect(attempts).toBe(2);
});

test("refreshes once on a 401 and replays the original request", async () => {
  let meCalls = 0;
  let refreshCalls = 0;
  server.use(
    http.get("/api/me", () => {
      meCalls += 1;
      if (meCalls === 1) {
        return HttpResponse.json({ title: "expired", status: 401, errorCode: "x" }, { status: 401 });
      }
      return HttpResponse.json({ firstName: "Akmal" });
    }),
    http.post("/api/auth/refresh", () => {
      refreshCalls += 1;
      return HttpResponse.json({ ...authTokens, accessToken: "refreshed" });
    }),
  );

  const onResult = vi.fn();
  render(signedIn("signed-init-data", <Probe onResult={onResult} />));

  await waitFor(() => expect(onResult).toHaveBeenCalledWith("Akmal"));
  expect(refreshCalls).toBe(1);
  expect(meCalls).toBe(2);
  expect(getAccessToken()).toBe("refreshed");
});

test("re-exchanges initData when the refresh token is rejected", async () => {
  let meCalls = 0;
  let exchanges = 0;
  server.use(
    http.get("/api/me", () => {
      meCalls += 1;
      if (meCalls === 1) {
        return HttpResponse.json({ title: "expired", status: 401, errorCode: "x" }, { status: 401 });
      }
      return HttpResponse.json({ firstName: "Akmal" });
    }),
    http.post("/api/auth/refresh", () =>
      HttpResponse.json(
        { title: "gone", status: 401, errorCode: "auth.refresh_token_invalid" },
        { status: 401 },
      ),
    ),
    http.post("/api/auth/telegram", () => {
      exchanges += 1;
      return HttpResponse.json({ ...authTokens, accessToken: `exchanged-${exchanges}` });
    }),
  );

  const onResult = vi.fn();
  render(signedIn("signed-init-data", <Probe onResult={onResult} />));

  await waitFor(() => expect(onResult).toHaveBeenCalledWith("Akmal"));
  // One exchange at startup, a second to recover from the dead refresh token.
  expect(exchanges).toBe(2);
  expect(getAccessToken()).toBe("exchanged-2");
});

test("two concurrent 401s trigger exactly one refresh", async () => {
  const expired = new Set<string>();
  let refreshCalls = 0;

  server.use(
    http.get("/api/me", ({ request }) => {
      // Each of the two probes gets one 401, so both race into onUnauthorized together.
      const marker = request.headers.get("x-probe") ?? "none";
      if (!expired.has(marker)) {
        expired.add(marker);
        return HttpResponse.json({ title: "expired", status: 401, errorCode: "x" }, { status: 401 });
      }
      return HttpResponse.json({ firstName: "Akmal" });
    }),
    http.post("/api/auth/refresh", async () => {
      refreshCalls += 1;
      // A real refresh is not instant; the delay is what lets the second caller find the first in
      // flight rather than finishing before it starts.
      await new Promise((resolve) => setTimeout(resolve, 20));
      return HttpResponse.json({ ...authTokens, accessToken: "refreshed-once" });
    }),
  );

  function TwoProbes({ onResult }: { onResult: (value: string) => void }) {
    const api = useApi();
    useEffect(() => {
      void Promise.all([
        api.request<{ firstName: string }>("/me", { headers: { "x-probe": "a" } }),
        api.request<{ firstName: string }>("/me", { headers: { "x-probe": "b" } }),
      ]).then(() => onResult("both"));
    }, [api, onResult]);
    return null;
  }

  const onResult = vi.fn();
  render(signedIn("signed-init-data", <TwoProbes onResult={onResult} />));

  await waitFor(() => expect(onResult).toHaveBeenCalledWith("both"));
  // Two rotations would waste one and, on a backend with reuse detection, look like a replay.
  expect(refreshCalls).toBe(1);
});

test("a 500 during refresh is surfaced rather than silently dropping the session", async () => {
  server.use(
    http.get("/api/me", () => HttpResponse.json({ title: "expired", status: 401, errorCode: "x" }, { status: 401 })),
    http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 500 })),
  );

  const onResult = vi.fn();
  render(signedIn("signed-init-data", <Probe onResult={onResult} />));

  await waitFor(() => expect(onResult).toHaveBeenCalled());
  expect(String(onResult.mock.calls[0]![0])).toContain("error:");
});

test("useApi outside an AuthProvider is a clear error rather than a null dereference", () => {
  function Orphan() {
    useApi();
    return null;
  }

  // React re-throws a render error out of render(), so this needs no error boundary. The point is
  // the message: a missing provider should say so, not fail later on a null client.
  expect(() => render(<Orphan />)).toThrow(/AuthProvider/);
});
