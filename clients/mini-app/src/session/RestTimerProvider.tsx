import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useTelegram } from "../telegram/TelegramProvider";

/**
 * The rest countdown.
 *
 * Holds a **target timestamp**, not a decrementing counter. A webview in a pocket is suspended,
 * and an interval that subtracts from a number would simply stop, so the athlete would come back
 * to a timer that still read two minutes. `targetAt - Date.now()` is correct the instant the
 * webview wakes, however long it was asleep.
 *
 * It lives above the router so navigating between screens does not kill it, and it is advisory
 * throughout: dismissable, and never blocking the next set.
 */

interface RestTimer {
  start: (seconds: number, label: string) => void;
  dismiss: () => void;
  /** Milliseconds left, or null when nothing is running. Never negative. */
  remainingMs: number | null;
  label: string | null;
  totalMs: number | null;
}

const RestTimerContext = createContext<RestTimer | null>(null);

const TICK_MS = 250;

interface TimerState {
  targetAt: number;
  totalMs: number;
  label: string;
}

export function RestTimerProvider({ children }: { children: ReactNode }) {
  const telegram = useTelegram();
  const [timer, setTimer] = useState<TimerState | null>(null);
  const [now, setNow] = useState(() => Date.now());

  // Guards the haptic so a re-render cannot fire it twice for one countdown.
  const notifiedFor = useRef<number | null>(null);

  const start = useCallback((seconds: number, label: string) => {
    const totalMs = Math.max(0, seconds) * 1000;
    const targetAt = Date.now() + totalMs;
    notifiedFor.current = null;
    // Replaces any running timer rather than stacking: one rest period at a time.
    setTimer({ targetAt, totalMs, label });
    setNow(Date.now());
  }, []);

  const dismiss = useCallback(() => {
    setTimer(null);
    notifiedFor.current = null;
  }, []);

  useEffect(() => {
    if (timer === null) return;

    const id = setInterval(() => setNow(Date.now()), TICK_MS);
    return () => clearInterval(id);
  }, [timer]);

  const remainingMs = timer === null ? null : Math.max(0, timer.targetAt - now);

  useEffect(() => {
    if (timer === null || remainingMs === null || remainingMs > 0) return;
    if (notifiedFor.current === timer.targetAt) return;

    notifiedFor.current = timer.targetAt;
    // Haptic only. No audio -- the phone is in a pocket and the gym is loud -- and a Mini App
    // cannot raise a notification.
    telegram.notifySuccess();
  }, [timer, remainingMs, telegram]);

  const value = useMemo<RestTimer>(
    () => ({
      start,
      dismiss,
      remainingMs,
      label: timer?.label ?? null,
      totalMs: timer?.totalMs ?? null,
    }),
    [start, dismiss, remainingMs, timer],
  );

  return <RestTimerContext.Provider value={value}>{children}</RestTimerContext.Provider>;
}

export function useRestTimer(): RestTimer {
  const value = useContext(RestTimerContext);
  if (value === null) {
    throw new Error("useRestTimer must be used inside a RestTimerProvider.");
  }
  return value;
}

export function formatRemaining(ms: number): string {
  const totalSeconds = Math.ceil(ms / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}
