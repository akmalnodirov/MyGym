import { useEffect, useRef } from "react";
import { useTelegram } from "./TelegramProvider";

/**
 * Wires Telegram's back button to history.
 *
 * Shown when there is somewhere to go back to and hidden at the root, because a back button that
 * does nothing is worse than no back button. Like the main button it is a single global object, so
 * the cleanup unsubscribing is what stops a stale handler navigating from a screen that is gone.
 */
export function useBackButton({ canGoBack, onBack }: { canGoBack: boolean; onBack: () => void }): void {
  const runtime = useTelegram();
  const handler = useRef(onBack);
  handler.current = onBack;

  useEffect(() => {
    const button = runtime.backButton;

    if (!canGoBack) {
      button.hide();
      return;
    }

    button.show();
    const unsubscribe = button.onClick(() => handler.current());

    return () => {
      unsubscribe();
      button.hide();
    };
  }, [runtime, canGoBack]);
}
