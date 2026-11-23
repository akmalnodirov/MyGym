import { useEffect } from "react";
import { useTelegram } from "./TelegramProvider";

/**
 * Asks Telegram to confirm before closing while `active` is true.
 *
 * Used only while a workout is in progress. Without it a swipe-down mid-workout closes the app
 * silently; the session survives server-side, but to the athlete it reads as losing their workout.
 * Disabled on unmount so the confirmation never outlives the session that justified it.
 */
export function useClosingConfirmation(active: boolean): void {
  const runtime = useTelegram();

  useEffect(() => {
    if (!active) return;

    runtime.enableClosingConfirmation();
    return () => runtime.disableClosingConfirmation();
  }, [runtime, active]);
}
