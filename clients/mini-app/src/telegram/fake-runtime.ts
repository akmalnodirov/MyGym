import type { TelegramButton, TelegramRuntime } from "./runtime";

/**
 * A development and test stand-in for the Telegram runtime.
 *
 * It fakes the *platform*, never authentication. `initData` is signed with a development bot token
 * using the same HMAC derivation the backend validates, so the real signature-checked
 * `/api/auth/telegram` path runs end to end in an ordinary browser. A `?dev=1` that minted a token
 * without a signature would be a production auth bypass one misconfiguration away from being
 * live, and would leave the real path never exercised.
 */

export interface FakeButton extends TelegramButton {
  /** Test-only: fires every registered handler, as a real tap would. */
  click(): void;
  readonly text: string;
  readonly visible: boolean;
  readonly progressShown: boolean;
}

export interface FakeTelegramRuntime extends TelegramRuntime {
  readonly backButton: FakeButton;
  readonly mainButton: FakeButton;
  readonly successNotifications: number;
  readonly closingConfirmationEnabled: boolean;
  readonly expandCount: number;
  setInitData(initData: string): void;
}

function createFakeButton(): FakeButton {
  const handlers = new Set<() => void>();
  const state = { text: "", visible: false, progressShown: false };

  return {
    show: () => {
      state.visible = true;
    },
    hide: () => {
      state.visible = false;
    },
    setText: (text: string) => {
      state.text = text;
    },
    showProgress: () => {
      state.progressShown = true;
    },
    hideProgress: () => {
      state.progressShown = false;
    },
    onClick: (callback: () => void) => {
      handlers.add(callback);
      return () => {
        handlers.delete(callback);
      };
    },
    click: () => {
      // Copied before iterating: a handler that navigates may unsubscribe during the loop.
      for (const handler of [...handlers]) handler();
    },
    get text() {
      return state.text;
    },
    get visible() {
      return state.visible;
    },
    get progressShown() {
      return state.progressShown;
    },
  };
}

const DEV_USER = {
  id: 1234567,
  first_name: "Akmal",
  username: "akmal",
  language_code: "en",
};

/**
 * Builds a signed `initData` string using Telegram's derivation:
 * `HMAC_SHA256(key: HMAC_SHA256(key: "WebAppData", message: botToken), message: dataCheckString)`,
 * where the data-check-string is the parameters except `hash`, sorted by key, joined with newlines.
 * This mirrors `src/Shared/MyGym.Shared.Infrastructure/Auth/TelegramInitDataValidator.cs`.
 *
 * `authDate` is a parameter rather than read from the clock so tests are deterministic.
 */
export async function signInitData(options: {
  botToken: string;
  authDate: number;
  user?: unknown;
}): Promise<string> {
  const params = new URLSearchParams({
    auth_date: String(options.authDate),
    query_id: "AAE_dev_query_id",
    user: JSON.stringify(options.user ?? DEV_USER),
  });

  const dataCheckString = [...params.entries()]
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    .map(([key, value]) => `${key}=${value}`)
    .join("\n");

  const encoder = new TextEncoder();

  const seed = await crypto.subtle.importKey(
    "raw",
    encoder.encode("WebAppData"),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const secret = await crypto.subtle.sign("HMAC", seed, encoder.encode(options.botToken));

  const signingKey = await crypto.subtle.importKey("raw", secret, { name: "HMAC", hash: "SHA-256" }, false, [
    "sign",
  ]);
  const signature = await crypto.subtle.sign("HMAC", signingKey, encoder.encode(dataCheckString));

  const hash = [...new Uint8Array(signature)].map((b) => b.toString(16).padStart(2, "0")).join("");

  // Sorted here too, so the string the backend reconstructs matches what was signed regardless of
  // insertion order.
  const sorted = new URLSearchParams([...params.entries()].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
  sorted.set("hash", hash);
  return sorted.toString();
}

/**
 * Telegram's own theme defaults, so the fake exercises the same CSS custom properties the real
 * runtime sets rather than leaving the app unstyled in development.
 */
const FAKE_THEME: Record<string, string> = {
  bg_color: "#ffffff",
  text_color: "#000000",
  hint_color: "#707579",
  link_color: "#2481cc",
  button_color: "#2481cc",
  button_text_color: "#ffffff",
  secondary_bg_color: "#f1f1f1",
};

export function createFakeRuntime(options?: { initData?: string }): FakeTelegramRuntime {
  const backButton = createFakeButton();
  const mainButton = createFakeButton();
  const state = {
    initData: options?.initData ?? "",
    successNotifications: 0,
    closingConfirmationEnabled: false,
    expandCount: 0,
  };

  return {
    get initData() {
      return state.initData;
    },
    setInitData: (initData: string) => {
      state.initData = initData;
    },
    languageCode: DEV_USER.language_code,
    themeParams: FAKE_THEME,
    backButton,
    mainButton,
    isFake: true,
    expand: () => {
      state.expandCount += 1;
    },
    enableClosingConfirmation: () => {
      state.closingConfirmationEnabled = true;
    },
    disableClosingConfirmation: () => {
      state.closingConfirmationEnabled = false;
    },
    notifySuccess: () => {
      state.successNotifications += 1;
    },
    get successNotifications() {
      return state.successNotifications;
    },
    get closingConfirmationEnabled() {
      return state.closingConfirmationEnabled;
    },
    get expandCount() {
      return state.expandCount;
    },
  };
}

/**
 * The runtime the app should use, chosen once at startup.
 *
 * The fake is reached only when VITE_TELEGRAM_FAKE is set, which Vite statically replaces at build
 * time, so a production bundle cannot select it even if the variable were set at runtime.
 */
export async function resolveRuntime(): Promise<TelegramRuntime> {
  if (import.meta.env.VITE_TELEGRAM_FAKE === "1") {
    const botToken = import.meta.env.VITE_DEV_BOT_TOKEN;
    const runtime = createFakeRuntime();

    if (botToken !== undefined && botToken.length > 0) {
      runtime.setInitData(
        await signInitData({ botToken, authDate: Math.floor(Date.now() / 1000) }),
      );
    } else {
      // Left empty on purpose. With no development bot token there is nothing the backend could
      // verify, and a plausible-looking unsigned string would fail with a confusing signature
      // error instead of an obvious missing-configuration one.
      runtime.setInitData("");
    }

    return runtime;
  }

  const { createRealRuntime } = await import("./runtime");
  return createRealRuntime();
}
