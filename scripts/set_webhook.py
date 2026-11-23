#!/usr/bin/env python3
"""
Registers the bot's webhook with Telegram, and sets the chat menu button to open the Mini App.

Nothing in the application does this. The webhook endpoint exists and is secret-guarded, but
Telegram will not send a single update until the URL is registered through the Bot API, which is a
one-time call per deployment. This script makes it one command instead of a hand-assembled curl.

It also reads the registration back with getWebhookInfo and reports it, because setWebhook can
answer 200 while Telegram subsequently fails to deliver -- a bad certificate, an unreachable host --
and `last_error_message` is where that shows up.

Usage:
    BOT_TOKEN=123:ABC PUBLIC_URL=https://gym.example.com \\
    WEBHOOK_SECRET=<same as the API's Telegram:WebhookSecretToken> \\
      python3 scripts/set_webhook.py

    # Undo it (stop Telegram sending updates):
    BOT_TOKEN=123:ABC python3 scripts/set_webhook.py --delete

    # Look at the current state without changing anything:
    BOT_TOKEN=123:ABC python3 scripts/set_webhook.py --show

Environment:
    BOT_TOKEN        required  from @BotFather
    PUBLIC_URL       required  the HTTPS origin serving this stack, no trailing path
    WEBHOOK_SECRET   optional  must match the API's Telegram:WebhookSecretToken; generated and
                               printed if absent, so you can set it on the API
    MINIAPP_URL      optional  default PUBLIC_URL -- the Mini App's own URL
    MENU_BUTTON_TEXT optional  default "MyGym"
    SKIP_MENU_BUTTON optional  "1" to leave the chat menu button alone
"""

from __future__ import annotations

import json
import os
import secrets
import sys
import urllib.error
import urllib.parse
import urllib.request

API = "https://api.telegram.org"

for stream in (sys.stdout, sys.stderr):
    reconfigure = getattr(stream, "reconfigure", None)
    if reconfigure is not None:
        reconfigure(encoding="utf-8", errors="replace")


def fail(message: str) -> "NoReturn":  # type: ignore[name-defined]
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)


def call(token: str, method: str, payload: dict[str, object] | None = None) -> dict[str, object]:
    """Calls one Bot API method. Telegram reports failure in the body, not only in the status."""
    url = f"{API}/bot{token}/{method}"
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    headers = {"content-type": "application/json"} if data else {}

    request = urllib.request.Request(url, data=data, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(request) as response:
            body = json.loads(response.read())
    except urllib.error.HTTPError as error:
        try:
            body = json.loads(error.read())
        except Exception:
            fail(f"{method} failed with HTTP {error.code}")
    except urllib.error.URLError as error:
        fail(f"could not reach {API}: {error.reason}")

    if not body.get("ok"):
        description = str(body.get("description", "no description"))
        hint = ""
        # "Unauthorized" is what a wrong or revoked token actually produces, and it is the most
        # common first-run failure, so it gets the hint too -- not just descriptions that happen to
        # contain the word "token".
        if "unauthorized" in description.lower() or "token" in description.lower():
            hint = "\nCheck BOT_TOKEN -- it should look like 123456789:AA... from @BotFather."
        fail(f"{method}: {description}{hint}")

    return body


def require_https(label: str, value: str) -> str:
    parsed = urllib.parse.urlparse(value)
    if parsed.scheme != "https":
        fail(
            f"{label} must be https, got {value!r}.\n"
            "Telegram refuses a plain-http webhook and will not open a plain-http Mini App. For "
            "local testing use a tunnel (cloudflared, ngrok) and pass its https URL."
        )
    if not parsed.netloc:
        fail(f"{label} is not a URL: {value!r}")
    return value.rstrip("/")


def show(token: str) -> None:
    info = call(token, "getWebhookInfo")["result"]
    assert isinstance(info, dict)

    url = info.get("url") or "(none)"
    print(f"  url                   {url}")
    print(f"  custom certificate    {info.get('has_custom_certificate')}")
    print(f"  pending updates       {info.get('pending_update_count')}")
    if info.get("allowed_updates"):
        print(f"  allowed updates       {', '.join(info['allowed_updates'])}")  # type: ignore[arg-type]

    # The part worth reading: setWebhook can succeed while delivery fails.
    if info.get("last_error_message"):
        print(f"  LAST ERROR            {info['last_error_message']}")
        print("  ^ Telegram accepted the registration but cannot deliver. Usually an unreachable")
        print("    host, a certificate it will not trust, or the API not running.")
    else:
        print("  last error            none")


def main() -> None:
    token = os.environ.get("BOT_TOKEN")
    if not token:
        fail("BOT_TOKEN is required -- get one from @BotFather.")

    args = set(sys.argv[1:])

    if "--show" in args:
        print("Current webhook:")
        show(token)
        return

    if "--delete" in args:
        # drop_pending_updates, because anything queued was addressed to a deployment that is
        # going away; replaying it at the next one would be noise at best.
        call(token, "deleteWebhook", {"drop_pending_updates": True})
        print("Webhook deleted. Telegram will stop sending updates.")
        return

    public_url = os.environ.get("PUBLIC_URL")
    if not public_url:
        fail("PUBLIC_URL is required -- the HTTPS origin serving this stack.")
    public_url = require_https("PUBLIC_URL", public_url)

    miniapp_url = require_https("MINIAPP_URL", os.environ.get("MINIAPP_URL", public_url))

    secret = os.environ.get("WEBHOOK_SECRET")
    generated = secret is None or secret == ""
    if generated:
        secret = secrets.token_urlsafe(32)

    webhook_url = f"{public_url}/api/telegram/webhook"

    call(
        token,
        "setWebhook",
        {
            "url": webhook_url,
            "secret_token": secret,
            # Only what the handler acts on. Narrowing this is not cosmetic: the webhook endpoint
            # caps request bodies, and unfiltered updates include types far larger than a message.
            "allowed_updates": ["message"],
            "drop_pending_updates": True,
        },
    )
    print(f"Webhook registered: {webhook_url}")

    if os.environ.get("SKIP_MENU_BUTTON") != "1":
        # Without this the app opens only from /start's inline button. The menu button is the
        # persistent way in -- the one next to the message box in every chat with the bot.
        call(
            token,
            "setChatMenuButton",
            {
                "menu_button": {
                    "type": "web_app",
                    "text": os.environ.get("MENU_BUTTON_TEXT", "MyGym"),
                    "web_app": {"url": miniapp_url},
                }
            },
        )
        print(f"Menu button set to open {miniapp_url}")

    print()
    print("Verifying with getWebhookInfo:")
    show(token)

    print()
    if generated:
        print("A webhook secret was generated for you. The API must be configured with the SAME")
        print("value, or it will reject every update with a 401:")
        print()
        print(f"  TELEGRAM_WEBHOOK_SECRET={secret}")
        print()
        print("Put it in .env next to docker-compose.yml, or pass it as")
        print("Telegram__WebhookSecretToken, then restart the API and run this script again with")
        print("WEBHOOK_SECRET set to the same value.")
    else:
        print("Done. Send /start to the bot; it should reply with a button that opens the app.")


if __name__ == "__main__":
    main()
