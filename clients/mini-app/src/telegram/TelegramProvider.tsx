import { createContext, useContext, useEffect, useRef, type ReactNode } from "react";
import type { TelegramRuntime } from "./runtime";

const TelegramContext = createContext<TelegramRuntime | null>(null);

export function TelegramProvider({
  runtime,
  children,
}: {
  runtime: TelegramRuntime;
  children: ReactNode;
}) {
  const expanded = useRef(false);

  useEffect(() => {
    // Once per runtime, not once per mount: StrictMode double-invokes effects in development, and
    // expanding twice is harmless but the guard keeps the fake's expandCount honest for tests.
    if (expanded.current) return;
    expanded.current = true;
    runtime.expand();
  }, [runtime]);

  return <TelegramContext.Provider value={runtime}>{children}</TelegramContext.Provider>;
}

export function useTelegram(): TelegramRuntime {
  const runtime = useContext(TelegramContext);
  if (runtime === null) {
    throw new Error("useTelegram must be used inside a TelegramProvider.");
  }
  return runtime;
}
