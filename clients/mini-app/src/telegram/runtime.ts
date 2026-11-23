/**
 * The only Telegram surface the app depends on.
 *
 * `window.Telegram.WebApp` exists only inside Telegram, so every component depends on this
 * interface instead and the two implementations — the real one here, the fake in
 * `fake-runtime.ts` — are the only files that know the difference. Nothing outside this directory
 * may touch `window.Telegram`.
 */

export interface TelegramButton {
  show(): void;
  hide(): void;
  setText(text: string): void;
  showProgress(): void;
  hideProgress(): void;
  /**
   * Registers a click handler and returns its unsubscribe function.
   *
   * Returning the unsubscribe rather than offering a separate `offClick` is deliberate. The Back
   * and Main buttons are single global objects, so a handler left bound after its screen unmounts
   * fires a stale action against a screen that no longer exists — the canonical bug of this
   * platform. Handing back the cleanup makes it impossible to forget at the call site.
   */
  onClick(callback: () => void): () => void;
}

export interface TelegramRuntime {
  /** The signed query string Telegram provides; empty outside Telegram. */
  readonly initData: string;
  readonly languageCode: string | null;
  readonly themeParams: Readonly<Record<string, string>>;
  readonly backButton: TelegramButton;
  readonly mainButton: TelegramButton;
  /** True for the development fake, so the provider can render on-page stand-in controls. */
  readonly isFake: boolean;
  expand(): void;
  enableClosingConfirmation(): void;
  disableClosingConfirmation(): void;
  notifySuccess(): void;
}

// ── The shape of the real SDK, declared rather than imported ───────────────
// telegram-web-app.js is loaded from telegram.org by index.html (Telegram requires it from their
// origin and ships no supported npm package). Only the members used below are declared; widening
// this is a deliberate act, not an accident of an upstream release.

interface TelegramWebAppButton {
  show(): void;
  hide(): void;
  setText?(text: string): void;
  showProgress?(leaveActive?: boolean): void;
  hideProgress?(): void;
  onClick(callback: () => void): void;
  offClick(callback: () => void): void;
}

interface TelegramWebApp {
  initData: string;
  initDataUnsafe?: { user?: { language_code?: string } };
  themeParams?: Record<string, string>;
  BackButton: TelegramWebAppButton;
  MainButton: TelegramWebAppButton;
  expand(): void;
  enableClosingConfirmation(): void;
  disableClosingConfirmation(): void;
  HapticFeedback?: {
    notificationOccurred(type: "error" | "success" | "warning"): void;
  };
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TelegramWebApp };
  }
}

export function getTelegramWebApp(): TelegramWebApp | null {
  return window.Telegram?.WebApp ?? null;
}

function adaptButton(button: TelegramWebAppButton): TelegramButton {
  return {
    show: () => button.show(),
    hide: () => button.hide(),
    // setText and the progress methods exist on MainButton but not on BackButton. Calling a
    // missing one would throw and take the screen down over a cosmetic concern, so they no-op.
    setText: (text) => button.setText?.(text),
    showProgress: () => button.showProgress?.(),
    hideProgress: () => button.hideProgress?.(),
    onClick: (callback) => {
      button.onClick(callback);
      return () => button.offClick(callback);
    },
  };
}

export function createRealRuntime(): TelegramRuntime {
  const webApp = getTelegramWebApp();
  if (webApp === null) {
    throw new Error(
      "window.Telegram.WebApp is not available. Open this app inside Telegram, or run the " +
        "development server with VITE_TELEGRAM_FAKE=1.",
    );
  }

  return {
    initData: webApp.initData,
    languageCode: webApp.initDataUnsafe?.user?.language_code ?? null,
    themeParams: webApp.themeParams ?? {},
    backButton: adaptButton(webApp.BackButton),
    mainButton: adaptButton(webApp.MainButton),
    isFake: false,
    expand: () => webApp.expand(),
    enableClosingConfirmation: () => webApp.enableClosingConfirmation(),
    disableClosingConfirmation: () => webApp.disableClosingConfirmation(),
    notifySuccess: () => webApp.HapticFeedback?.notificationOccurred("success"),
  };
}
