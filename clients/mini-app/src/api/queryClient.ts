import { QueryClient } from "@tanstack/react-query";
import { ApiError } from "./ApiError";

const MAX_ATTEMPTS = 3;

/**
 * Decides whether a failure is worth repeating.
 *
 * The default policy retries everything three times, which on a 404 means three pointless
 * requests for every dead link, and on a 422 means sending the same rejected payload again. Only
 * failures that could plausibly succeed unchanged are retried.
 */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (failureCount >= MAX_ATTEMPTS) return false;

  // Not an ApiError means the server never answered — a dropped connection in a basement gym,
  // which is exactly the case retrying exists for.
  if (!(error instanceof ApiError)) return true;

  if (error.status === 429) return true;
  if (error.status >= 500) return true;

  return false;
}

export function retryDelay(attemptIndex: number, error: unknown): number {
  // A server that said how long to wait knows better than our backoff curve.
  if (error instanceof ApiError && error.retryAfterMs !== null) return error.retryAfterMs;
  return Math.min(500 * 2 ** attemptIndex, 8000);
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: shouldRetry,
        retryDelay,
        // The phone is in a pocket between sets and the webview suspends; refetching when it wakes
        // is how the screen catches up with anything logged elsewhere.
        refetchOnWindowFocus: true,
        refetchOnReconnect: true,
        staleTime: 30_000,
      },
      mutations: {
        retry: shouldRetry,
        retryDelay,
      },
    },
  });
}
