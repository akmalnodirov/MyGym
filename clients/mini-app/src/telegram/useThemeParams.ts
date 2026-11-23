import { useEffect } from "react";
import { useTelegram } from "./TelegramProvider";

/**
 * Publishes Telegram's theme as CSS custom properties on the document root, so the app follows the
 * user's own Telegram theme instead of shipping its own light/dark decision.
 *
 * `bg_color` becomes `--tg-bg-color`, and so on for whatever keys Telegram sends -- the set
 * differs by client version, so the mapping is mechanical rather than an allow-list that would
 * silently drop new keys.
 */
export function useThemeParams(): void {
  const runtime = useTelegram();

  useEffect(() => {
    const root = document.documentElement;
    const applied: string[] = [];

    for (const [key, value] of Object.entries(runtime.themeParams)) {
      const property = `--tg-${key.replace(/_/g, "-")}`;
      root.style.setProperty(property, value);
      applied.push(property);
    }

    return () => {
      for (const property of applied) root.style.removeProperty(property);
    };
  }, [runtime]);
}
