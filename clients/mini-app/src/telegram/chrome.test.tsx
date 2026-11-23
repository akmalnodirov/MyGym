import { describe, expect, test, vi } from "vitest";
import { render } from "@testing-library/react";
import type { ReactNode } from "react";
import { TelegramProvider } from "./TelegramProvider";
import { useMainButton } from "./useMainButton";
import { useBackButton } from "./useBackButton";
import { useThemeParams } from "./useThemeParams";
import { useClosingConfirmation } from "./useClosingConfirmation";
import { createFakeRuntime, type FakeTelegramRuntime } from "./fake-runtime";

function wrap(runtime: FakeTelegramRuntime, children: ReactNode) {
  return <TelegramProvider runtime={runtime}>{children}</TelegramProvider>;
}

describe("useMainButton", () => {
  function Screen({ label, onClick }: { label: string; onClick: () => void }) {
    useMainButton({ text: label, onClick });
    return null;
  }

  test("a click reaches only the currently mounted screen", () => {
    const runtime = createFakeRuntime();
    const first = vi.fn();
    const second = vi.fn();

    const view = render(wrap(runtime, <Screen label="Start" onClick={first} />));
    view.rerender(wrap(runtime, <Screen label="Finish" onClick={second} />));

    runtime.mainButton.click();

    expect(second).toHaveBeenCalledTimes(1);
    expect(first).not.toHaveBeenCalled();
  });

  test("unmounting removes the handler, so a later click fires nothing", () => {
    const runtime = createFakeRuntime();
    const onClick = vi.fn();

    render(wrap(runtime, <Screen label="Start" onClick={onClick} />)).unmount();
    runtime.mainButton.click();

    expect(onClick).not.toHaveBeenCalled();
  });

  test("unmounting hides the button, so it does not linger on the next screen", () => {
    const runtime = createFakeRuntime();

    const view = render(wrap(runtime, <Screen label="Start" onClick={() => {}} />));
    expect(runtime.mainButton.visible).toBe(true);

    view.unmount();
    expect(runtime.mainButton.visible).toBe(false);
  });

  test("the button text follows the mounted screen", () => {
    const runtime = createFakeRuntime();

    const view = render(wrap(runtime, <Screen label="Start workout" onClick={() => {}} />));
    expect(runtime.mainButton.text).toBe("Start workout");

    view.rerender(wrap(runtime, <Screen label="Finish workout" onClick={() => {}} />));
    expect(runtime.mainButton.text).toBe("Finish workout");
  });

  test("a changing onClick identity does not accumulate handlers", () => {
    const runtime = createFakeRuntime();
    const onClick = vi.fn();

    function Rerendering({ tick }: { tick: number }) {
      // A fresh closure on every render, which is what a real screen produces.
      useMainButton({ text: "Log", onClick: () => onClick(tick) });
      return null;
    }

    const view = render(wrap(runtime, <Rerendering tick={1} />));
    view.rerender(wrap(runtime, <Rerendering tick={2} />));
    view.rerender(wrap(runtime, <Rerendering tick={3} />));

    runtime.mainButton.click();

    // One handler, holding the latest callback -- not three handlers firing together.
    expect(onClick).toHaveBeenCalledTimes(1);
    expect(onClick).toHaveBeenCalledWith(3);
  });

  test("loading shows progress and clearing it hides progress", () => {
    const runtime = createFakeRuntime();

    function Loading({ loading }: { loading: boolean }) {
      useMainButton({ text: "Finish", onClick: () => {}, loading });
      return null;
    }

    const view = render(wrap(runtime, <Loading loading={true} />));
    expect(runtime.mainButton.progressShown).toBe(true);

    view.rerender(wrap(runtime, <Loading loading={false} />));
    expect(runtime.mainButton.progressShown).toBe(false);
  });

  test("visible=false hides the button without unmounting the screen", () => {
    const runtime = createFakeRuntime();

    function Conditional({ visible }: { visible: boolean }) {
      useMainButton({ text: "Finish", onClick: () => {}, visible });
      return null;
    }

    render(wrap(runtime, <Conditional visible={false} />));
    expect(runtime.mainButton.visible).toBe(false);
  });
});

describe("useBackButton", () => {
  function Screen({ canGoBack, onBack }: { canGoBack: boolean; onBack: () => void }) {
    useBackButton({ canGoBack, onBack });
    return null;
  }

  test("is hidden at the root and shown when there is somewhere to go back to", () => {
    const runtime = createFakeRuntime();

    const view = render(wrap(runtime, <Screen canGoBack={false} onBack={() => {}} />));
    expect(runtime.backButton.visible).toBe(false);

    view.rerender(wrap(runtime, <Screen canGoBack={true} onBack={() => {}} />));
    expect(runtime.backButton.visible).toBe(true);
  });

  test("a click navigates back", () => {
    const runtime = createFakeRuntime();
    const onBack = vi.fn();

    render(wrap(runtime, <Screen canGoBack={true} onBack={onBack} />));
    runtime.backButton.click();

    expect(onBack).toHaveBeenCalledTimes(1);
  });

  test("no handler is registered while hidden, so a stray click does nothing", () => {
    const runtime = createFakeRuntime();
    const onBack = vi.fn();

    render(wrap(runtime, <Screen canGoBack={false} onBack={onBack} />));
    runtime.backButton.click();

    expect(onBack).not.toHaveBeenCalled();
  });

  test("unmounting removes the handler", () => {
    const runtime = createFakeRuntime();
    const onBack = vi.fn();

    render(wrap(runtime, <Screen canGoBack={true} onBack={onBack} />)).unmount();
    runtime.backButton.click();

    expect(onBack).not.toHaveBeenCalled();
  });
});

describe("TelegramProvider", () => {
  test("expands the viewport once at launch", () => {
    const runtime = createFakeRuntime();

    render(wrap(runtime, null));

    // Once, not twice: StrictMode double-invokes effects in development.
    expect(runtime.expandCount).toBe(1);
  });
});

describe("useThemeParams", () => {
  function Themed() {
    useThemeParams();
    return null;
  }

  test("publishes Telegram's theme as CSS custom properties", () => {
    const runtime = createFakeRuntime();

    render(wrap(runtime, <Themed />));

    expect(document.documentElement.style.getPropertyValue("--tg-bg-color")).toBe("#ffffff");
    expect(document.documentElement.style.getPropertyValue("--tg-button-color")).toBe("#2481cc");
    expect(document.documentElement.style.getPropertyValue("--tg-secondary-bg-color")).toBe("#f1f1f1");
  });

  test("removes the properties on unmount rather than leaving them on the document", () => {
    const runtime = createFakeRuntime();

    render(wrap(runtime, <Themed />)).unmount();

    expect(document.documentElement.style.getPropertyValue("--tg-bg-color")).toBe("");
  });
});

describe("useClosingConfirmation", () => {
  function Session({ active }: { active: boolean }) {
    useClosingConfirmation(active);
    return null;
  }

  test("is enabled while a workout is in progress and disabled when it ends", () => {
    const runtime = createFakeRuntime();

    const view = render(wrap(runtime, <Session active={true} />));
    expect(runtime.closingConfirmationEnabled).toBe(true);

    view.rerender(wrap(runtime, <Session active={false} />));
    expect(runtime.closingConfirmationEnabled).toBe(false);
  });

  test("is disabled on unmount, so it never outlives the session that justified it", () => {
    const runtime = createFakeRuntime();

    render(wrap(runtime, <Session active={true} />)).unmount();

    expect(runtime.closingConfirmationEnabled).toBe(false);
  });

  test("is never enabled for a read-only session", () => {
    const runtime = createFakeRuntime();

    render(wrap(runtime, <Session active={false} />));

    expect(runtime.closingConfirmationEnabled).toBe(false);
  });
});
