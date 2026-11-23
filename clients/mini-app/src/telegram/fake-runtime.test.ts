import { expect, test } from "vitest";
import { createFakeRuntime, signInitData } from "./fake-runtime";

const BOT_TOKEN = "123456:development-bot-token";

test("the signed initData is a sorted query string with a 64-hex hash", async () => {
  const initData = await signInitData({ botToken: BOT_TOKEN, authDate: 1764000000 });
  const params = new URLSearchParams(initData);

  expect(params.get("hash")).toMatch(/^[0-9a-f]{64}$/);
  expect(params.get("auth_date")).toBe("1764000000");
  expect(JSON.parse(params.get("user") ?? "{}")).toHaveProperty("id");

  // The backend rebuilds the data-check-string by sorting the parameters, so the signed string
  // must already be in that order for the two to agree.
  const keys = [...params.keys()].filter((k) => k !== "hash");
  expect(keys).toEqual([...keys].sort());
});

test("signing is deterministic for the same token and auth_date", async () => {
  const first = await signInitData({ botToken: BOT_TOKEN, authDate: 1764000000 });
  const second = await signInitData({ botToken: BOT_TOKEN, authDate: 1764000000 });

  expect(first).toBe(second);
});

test("a different bot token produces a different hash", async () => {
  const mine = new URLSearchParams(await signInitData({ botToken: BOT_TOKEN, authDate: 1764000000 }));
  const theirs = new URLSearchParams(await signInitData({ botToken: "999:other", authDate: 1764000000 }));

  // If these matched, the fake would be producing signatures any token could forge, and the
  // backend's HMAC check would be proving nothing in development.
  expect(mine.get("hash")).not.toBe(theirs.get("hash"));
});

test("a different auth_date produces a different hash", async () => {
  const early = new URLSearchParams(await signInitData({ botToken: BOT_TOKEN, authDate: 1764000000 }));
  const later = new URLSearchParams(await signInitData({ botToken: BOT_TOKEN, authDate: 1764000001 }));

  expect(early.get("hash")).not.toBe(later.get("hash"));
});

test("the hash covers the user payload, so tampering invalidates it", async () => {
  const original = await signInitData({ botToken: BOT_TOKEN, authDate: 1764000000 });
  const tampered = await signInitData({
    botToken: BOT_TOKEN,
    authDate: 1764000000,
    user: { id: 7777777, first_name: "Someone", username: "else", language_code: "en" },
  });

  expect(new URLSearchParams(original).get("hash")).not.toBe(new URLSearchParams(tampered).get("hash"));
});

test("the fake exposes Telegram's default theme so the app is styled in development", () => {
  const runtime = createFakeRuntime();

  expect(runtime.themeParams["bg_color"]).toBe("#ffffff");
  expect(runtime.isFake).toBe(true);
});

test("the fake starts with no initData, since an unsigned string would fail confusingly", () => {
  expect(createFakeRuntime().initData).toBe("");
  expect(createFakeRuntime({ initData: "preset" }).initData).toBe("preset");
});

test("notifySuccess is counted, so the rest timer's haptic can be asserted", () => {
  const runtime = createFakeRuntime();

  runtime.notifySuccess();
  runtime.notifySuccess();

  expect(runtime.successNotifications).toBe(2);
});
