import { beforeEach, describe, expect, test, vi } from "vitest";
import { createApiClient } from "./client";
import { ApiError } from "./ApiError";

function problem(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/problem+json" },
  });
}

function ok(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "content-type": "application/json" },
  });
}

const noRefresh = async (): Promise<boolean> => false;

describe("createApiClient", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  test("parses a ProblemDetails body into an ApiError carrying its errorCode", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        problem(409, {
          title: "A workout is already in progress.",
          status: 409,
          type: "https://mygym.app/errors/session.already_in_progress",
          errorCode: "session.already_in_progress",
        }),
      ),
    );
    const client = createApiClient({ baseUrl: "/api", getToken: () => "t", onUnauthorized: noRefresh });

    const error = (await client.request("/sessions").catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.errorCode).toBe("session.already_in_progress");
    expect(error.status).toBe(409);
    expect(error.title).toBe("A workout is already in progress.");
  });

  test("survives a non-2xx whose body is not JSON", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response("<html>502 Bad Gateway</html>", {
          status: 502,
          headers: { "content-type": "text/html" },
        }),
      ),
    );
    const client = createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh });

    const error = (await client.request("/sessions").catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(502);
    expect(error.errorCode).toBeNull();
  });

  test("resolves to null for 204 rather than failing on an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    const client = createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh });

    await expect(client.request("/sessions/current")).resolves.toBeNull();
  });

  test("sends a bearer token when one is available and omits the header when not", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(ok({})));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient({ baseUrl: "/api", getToken: () => "abc", onUnauthorized: noRefresh }).request("/me");
    expect(new Headers((fetchMock.mock.calls[0]![1] as RequestInit).headers).get("authorization")).toBe(
      "Bearer abc",
    );

    await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh }).request("/me");
    expect(new Headers((fetchMock.mock.calls[1]![1] as RequestInit).headers).has("authorization")).toBe(false);
  });

  test("joins the base URL and the path without doubling or dropping a slash", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(ok({})));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh }).request("/me");
    await createApiClient({ baseUrl: "/api/", getToken: () => null, onUnauthorized: noRefresh }).request("/me");

    expect(fetchMock.mock.calls[0]![0]).toBe("/api/me");
    expect(fetchMock.mock.calls[1]![0]).toBe("/api/me");
  });

  test("on 401 asks onUnauthorized to refresh and replays the request exactly once", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(problem(401, { errorCode: "auth.refresh_token_invalid", title: "x" }))
      .mockResolvedValueOnce(ok({ id: "s1" }));
    vi.stubGlobal("fetch", fetchMock);
    const onUnauthorized = vi.fn<() => Promise<boolean>>().mockResolvedValue(true);

    const result = await createApiClient({ baseUrl: "/api", getToken: () => "t", onUnauthorized }).request<{
      id: string;
    }>("/sessions/s1");

    expect(result).toEqual({ id: "s1" });
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  test("replays with the refreshed token, not the stale one", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(problem(401, { errorCode: "x", title: "x" }))
      .mockResolvedValueOnce(ok({}));
    vi.stubGlobal("fetch", fetchMock);
    let token = "stale";

    await createApiClient({
      baseUrl: "/api",
      getToken: () => token,
      onUnauthorized: async () => {
        token = "fresh";
        return true;
      },
    }).request("/me");

    expect(new Headers((fetchMock.mock.calls[1]![1] as RequestInit).headers).get("authorization")).toBe(
      "Bearer fresh",
    );
  });

  test("does not replay when the refresh itself fails", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(problem(401, { errorCode: "auth.refresh_token_invalid", title: "x" }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(
      createApiClient({ baseUrl: "/api", getToken: () => "t", onUnauthorized: noRefresh }).request("/sessions"),
    ).rejects.toBeInstanceOf(ApiError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  test("does not refresh a second time when the replay is also a 401", async () => {
    const fetchMock = vi
      .fn()
      .mockImplementation(() => Promise.resolve(problem(401, { errorCode: "x", title: "x" })));
    vi.stubGlobal("fetch", fetchMock);
    const onUnauthorized = vi.fn<() => Promise<boolean>>().mockResolvedValue(true);

    await expect(
      createApiClient({ baseUrl: "/api", getToken: () => "t", onUnauthorized }).request("/me"),
    ).rejects.toBeInstanceOf(ApiError);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  test("serialises a json body and sets content-type", async () => {
    const fetchMock = vi.fn().mockResolvedValue(ok({}));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh }).request("/sessions", {
      method: "POST",
      json: { programId: "p1" },
    });

    const init = fetchMock.mock.calls[0]![1] as RequestInit;
    expect(init.body).toBe(JSON.stringify({ programId: "p1" }));
    expect(new Headers(init.headers).get("content-type")).toBe("application/json");
  });

  test("exposes Retry-After in milliseconds so the retry policy can honour it", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ title: "Too many requests" }), {
          status: 429,
          headers: { "content-type": "application/problem+json", "retry-after": "3" },
        }),
      ),
    );

    const error = (await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh })
      .request("/sessions")
      .catch((e: unknown) => e)) as ApiError;

    expect(error.status).toBe(429);
    expect(error.retryAfterMs).toBe(3000);
  });

  test("leaves retryAfterMs null when the header is absent or unparseable", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response("{}", { status: 429, headers: { "content-type": "application/json", "retry-after": "soon" } }),
      ),
    );

    const error = (await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh })
      .request("/sessions")
      .catch((e: unknown) => e)) as ApiError;

    expect(error.retryAfterMs).toBeNull();
  });

  test("wraps a network failure as an Error that is not an ApiError, so retry treats it as transient", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    const error = (await createApiClient({ baseUrl: "/api", getToken: () => null, onUnauthorized: noRefresh })
      .request("/sessions")
      .catch((e: unknown) => e)) as Error;

    expect(error).toBeInstanceOf(Error);
    expect(error).not.toBeInstanceOf(ApiError);
  });
});
