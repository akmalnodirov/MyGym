import { ApiError, parseRetryAfterMs } from "./ApiError";

export type TokenSource = () => string | null;

export interface ApiRequestInit extends Omit<RequestInit, "body"> {
  /** Serialised as the request body with an `application/json` content type. */
  json?: unknown;
}

export interface ApiClient {
  request<T>(path: string, init?: ApiRequestInit): Promise<T>;
}

export interface ApiClientOptions {
  baseUrl: string;
  getToken: TokenSource;
  /**
   * Called once when a request comes back 401. Resolving `true` means a fresh token is now
   * available and the request should be replayed; `false` means give up and surface the error.
   */
  onUnauthorized: () => Promise<boolean>;
}

function joinUrl(baseUrl: string, path: string): string {
  const base = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
  const tail = path.startsWith("/") ? path : `/${path}`;
  return `${base}${tail}`;
}

async function readErrorBody(response: Response): Promise<{ errorCode: string | null; title: string }> {
  // The body may be anything: ProblemDetails from our own handlers, a proxy's HTML error page, or
  // nothing at all. None of those may be allowed to throw past the caller, because the status is
  // the part we actually need.
  try {
    const text = await response.text();
    if (text.length === 0) return { errorCode: null, title: response.statusText };

    const parsed: unknown = JSON.parse(text);
    if (typeof parsed !== "object" || parsed === null) {
      return { errorCode: null, title: response.statusText };
    }

    const body = parsed as Record<string, unknown>;
    return {
      errorCode: typeof body["errorCode"] === "string" ? body["errorCode"] : null,
      title: typeof body["title"] === "string" ? body["title"] : response.statusText,
    };
  } catch {
    return { errorCode: null, title: response.statusText };
  }
}

async function readSuccessBody<T>(response: Response): Promise<T> {
  // 204 is a real answer, not an absence of one: GET /api/sessions/current uses it to mean "no
  // workout in progress", and every command endpoint uses it for success.
  if (response.status === 204) return null as T;

  const text = await response.text();
  if (text.length === 0) return null as T;
  return JSON.parse(text) as T;
}

export function createApiClient(options: ApiClientOptions): ApiClient {
  const { baseUrl, getToken, onUnauthorized } = options;

  function buildInit(init: ApiRequestInit | undefined): RequestInit {
    const headers = new Headers(init?.headers);

    const token = getToken();
    if (token !== null) headers.set("authorization", `Bearer ${token}`);

    const result: RequestInit = { ...init, headers };
    delete (result as { json?: unknown }).json;

    if (init?.json !== undefined) {
      headers.set("content-type", "application/json");
      result.body = JSON.stringify(init.json);
    }

    return result;
  }

  async function send(path: string, init: ApiRequestInit | undefined): Promise<Response> {
    return fetch(joinUrl(baseUrl, path), buildInit(init));
  }

  return {
    async request<T>(path: string, init?: ApiRequestInit): Promise<T> {
      let response = await send(path, init);

      if (response.status === 401) {
        // Exactly one refresh-and-replay. A counter would invite an off-by-one; a boolean cannot
        // express "twice", so the replay's own 401 is surfaced rather than refreshed again.
        const refreshed = await onUnauthorized();
        if (refreshed) response = await send(path, init);
      }

      if (!response.ok) {
        const { errorCode, title } = await readErrorBody(response);
        throw new ApiError({
          status: response.status,
          errorCode,
          title,
          retryAfterMs: parseRetryAfterMs(response.headers.get("retry-after")),
        });
      }

      return readSuccessBody<T>(response);
    },
  };
}
