/**
 * A non-2xx response from the API.
 *
 * The API answers failures with RFC 9457 ProblemDetails plus an `errorCode` extension carrying a
 * stable dotted code (`session.closed`, `program.not_found`, ...). Branch on `errorCode`: `title`
 * is a human-readable message that may be reworded, and status alone is ambiguous because one
 * status covers many codes — 409 by itself carries nine of them.
 *
 * A network failure is deliberately *not* an ApiError. The retry policy distinguishes "the server
 * answered and said no" from "we never reached the server", and only the second is worth retrying
 * unconditionally.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly errorCode: string | null;
  readonly title: string;
  /** `Retry-After` in milliseconds, when the server sent a parseable one. */
  readonly retryAfterMs: number | null;

  constructor(args: {
    status: number;
    errorCode: string | null;
    title: string;
    retryAfterMs: number | null;
  }) {
    super(`${args.status} ${args.errorCode ?? "unknown"}: ${args.title}`);
    this.name = "ApiError";
    this.status = args.status;
    this.errorCode = args.errorCode;
    this.title = args.title;
    this.retryAfterMs = args.retryAfterMs;
  }
}

/**
 * Parses a `Retry-After` header value. Only the delay-seconds form is handled; the HTTP-date form
 * is valid per the RFC but ASP.NET's rate limiter emits seconds, and guessing at clock skew to
 * interpret a date would be worse than falling back to the default backoff.
 */
export function parseRetryAfterMs(header: string | null): number | null {
  if (header === null) return null;
  const seconds = Number(header);
  if (!Number.isFinite(seconds) || seconds < 0) return null;
  return seconds * 1000;
}
