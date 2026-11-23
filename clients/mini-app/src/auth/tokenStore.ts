import type { components } from "../api/generated";

type AuthTokens = components["schemas"]["AuthTokensResponse"];

/**
 * Tokens live here and nowhere else: in memory, for the lifetime of the page.
 *
 * Nothing is written to localStorage, sessionStorage or a cookie. `initData` is available from the
 * Telegram runtime on every launch, so a cold start can always exchange it for a fresh pair —
 * which makes persistence pure cost: a stored refresh token is an XSS exfiltration target and a
 * source of cross-launch expiry edge cases, bought for nothing.
 */

let tokens: AuthTokens | null = null;

/** A single in-flight refresh, shared by every caller that sees a 401 at the same moment. */
let inFlightRefresh: Promise<boolean> | null = null;

export function getAccessToken(): string | null {
  return tokens?.accessToken ?? null;
}

export function getRefreshToken(): string | null {
  return tokens?.refreshToken ?? null;
}

export function setTokens(next: AuthTokens): void {
  tokens = next;
}

export function clearTokens(): void {
  tokens = null;
  inFlightRefresh = null;
}

export function hasTokens(): boolean {
  return tokens !== null;
}

/**
 * Runs `refresh` once even when several requests fail with 401 together.
 *
 * Three queries firing on a screen mount will all see the expired token. Without this, all three
 * would refresh, two of those rotations would be wasted, and on a backend with refresh-token reuse
 * detection they would look like a replay attack.
 */
export function coalesceRefresh(refresh: () => Promise<boolean>): Promise<boolean> {
  if (inFlightRefresh !== null) return inFlightRefresh;

  inFlightRefresh = refresh().finally(() => {
    inFlightRefresh = null;
  });

  return inFlightRefresh;
}
