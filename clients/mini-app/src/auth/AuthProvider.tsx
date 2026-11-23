import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { components } from "../api/generated";
import { ApiError } from "../api/ApiError";
import { createApiClient, type ApiClient } from "../api/client";
import { messageForCode } from "../errors/codes";
import { useTelegram } from "../telegram/TelegramProvider";
import { clearTokens, coalesceRefresh, getAccessToken, getRefreshToken, setTokens } from "./tokenStore";

type AuthTokens = components["schemas"]["AuthTokensResponse"];

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

const ApiContext = createContext<ApiClient | null>(null);

export function useApi(): ApiClient {
  const client = useContext(ApiContext);
  if (client === null) throw new Error("useApi must be used inside an AuthProvider.");
  return client;
}

type AuthState =
  | { phase: "signingIn" }
  | { phase: "signedIn" }
  | { phase: "failed"; message: string };

/**
 * Exchanges Telegram's `initData` for tokens before rendering anything that needs the API.
 *
 * There is no login screen and no development bypass: the real signature-checked
 * `/api/auth/telegram` endpoint runs in every environment, and development fakes the Telegram
 * runtime instead (see `src/telegram/fake-runtime.ts`).
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const telegram = useTelegram();
  const [state, setState] = useState<AuthState>({ phase: "signingIn" });
  const [attempt, setAttempt] = useState(0);

  // A bare client with no 401 handling, used for the auth endpoints themselves. Refreshing a
  // refresh call would recurse.
  const authClient = useMemo(
    () =>
      createApiClient({
        baseUrl: API_BASE_URL,
        getToken: () => null,
        onUnauthorized: async () => false,
      }),
    [],
  );

  const exchangeInitData = useCallback(async (): Promise<boolean> => {
    const tokens = await authClient.request<AuthTokens>("/auth/telegram", {
      method: "POST",
      json: { initData: telegram.initData },
    });
    setTokens(tokens);
    return true;
  }, [authClient, telegram.initData]);

  // Held in a ref so the client built below never has to be rebuilt when the callback identity
  // changes — rebuilding it would discard nothing important, but it would churn every consumer.
  const refreshRef = useRef<() => Promise<boolean>>(async () => false);

  refreshRef.current = async (): Promise<boolean> => {
    const refreshToken = getRefreshToken();

    if (refreshToken !== null) {
      try {
        const tokens = await authClient.request<AuthTokens>("/auth/refresh", {
          method: "POST",
          json: { refreshToken },
        });
        setTokens(tokens);
        return true;
      } catch (error) {
        // Anything other than a rejected token is a transient failure; surface it rather than
        // dropping the session over a flaky network.
        if (!(error instanceof ApiError) || error.status !== 401) throw error;
        clearTokens();
      }
    }

    // The refresh token is gone or rejected. Inside Telegram `initData` is always present, so
    // re-exchanging it is the recovery rather than an error state.
    try {
      return await exchangeInitData();
    } catch {
      clearTokens();
      return false;
    }
  };

  const client = useMemo(
    () =>
      createApiClient({
        baseUrl: API_BASE_URL,
        getToken: getAccessToken,
        onUnauthorized: () => coalesceRefresh(() => refreshRef.current()),
      }),
    [],
  );

  useEffect(() => {
    let cancelled = false;
    setState({ phase: "signingIn" });

    void (async () => {
      try {
        await exchangeInitData();
        if (!cancelled) setState({ phase: "signedIn" });
      } catch (error) {
        if (cancelled) return;
        const message =
          error instanceof ApiError
            ? messageForCode(error.errorCode)
            : "Could not reach MyGym. Check your connection and try again.";
        setState({ phase: "failed", message });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [exchangeInitData, attempt]);

  if (state.phase === "signingIn") {
    return (
      <div className="screen screen--centred" role="status" aria-live="polite">
        Signing in…
      </div>
    );
  }

  if (state.phase === "failed") {
    return (
      <div className="screen screen--centred" role="alert">
        <p>{state.message}</p>
        <button type="button" onClick={() => setAttempt((n) => n + 1)}>
          Try again
        </button>
      </div>
    );
  }

  return <ApiContext.Provider value={client}>{children}</ApiContext.Provider>;
}
