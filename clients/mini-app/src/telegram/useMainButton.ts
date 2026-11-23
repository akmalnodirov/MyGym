import { useEffect, useRef } from "react";
import { useTelegram } from "./TelegramProvider";

export interface MainButtonOptions {
  text: string;
  onClick: () => void;
  visible?: boolean;
  loading?: boolean;
}

/**
 * Declares the Telegram main button for the mounted screen.
 *
 * The button is a single global object, so this hook is its only owner: screens say what they want
 * it to read and do, and the cleanup both unsubscribes the handler and hides the button. Without
 * that cleanup a handler outlives its screen and the next tap runs a stale action.
 *
 * `onClick` is held in a ref so a new callback identity on every render does not resubscribe.
 */
export function useMainButton({ text, onClick, visible = true, loading = false }: MainButtonOptions): void {
  const runtime = useTelegram();
  const handler = useRef(onClick);
  handler.current = onClick;

  useEffect(() => {
    const button = runtime.mainButton;
    button.setText(text);

    if (visible) button.show();
    else button.hide();

    if (loading) button.showProgress();
    else button.hideProgress();

    const unsubscribe = button.onClick(() => handler.current());

    return () => {
      unsubscribe();
      button.hide();
    };
  }, [runtime, text, visible, loading]);
}
