#!/usr/bin/env python3
"""
Creates (or updates) a training program through the API and activates it.

There is no authoring UI in Mini App v1, so this is how a program gets into the database. It is
written against the public API rather than the database on purpose: the same domain validation an
athlete would hit applies here, so a program this script accepts is a program the app can run.

Authentication goes through the real signature-checked /api/auth/telegram endpoint. The script
builds an initData string and signs it with the bot token, using Telegram's own derivation, so
nothing here bypasses the check -- there is no back door to seed through.

Re-running is safe. A program with the same name is replaced via PUT rather than duplicated, so
editing the JSON and running again is the edit loop.

Usage:
    BOT_TOKEN=123:ABC TELEGRAM_USER_ID=123456789 python3 scripts/seed_program.py

Environment:
    BOT_TOKEN          required   the bot's token, used to sign initData
    TELEGRAM_USER_ID   required   your numeric Telegram user id (ask @userinfobot)
    API_BASE_URL       optional   default http://127.0.0.1:5199
    PROGRAM_FILE       optional   default scripts/programs/akmal-3day.json
    FIRST_NAME         optional   default "Athlete" -- only used on first sign-in
    SET_PROFILE        optional   "1" to also PATCH /api/me with the values below
    BODYWEIGHT         optional   used only with SET_PROFILE
    TIME_ZONE          optional   default Asia/Tashkent, used only with SET_PROFILE
"""

from __future__ import annotations

import hashlib
import hmac
import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent

# Windows consoles default to cp1252, which cannot encode an em dash — and the program's own day
# labels contain them ("Dushanba — Upper Body"), so printing a success summary would die with a
# UnicodeEncodeError and hide whatever actually happened. Replace rather than fail: a mangled
# character in a log line is nothing; losing the message is the problem.
for stream in (sys.stdout, sys.stderr):
    reconfigure = getattr(stream, "reconfigure", None)
    if reconfigure is not None:
        reconfigure(encoding="utf-8", errors="replace")


# ── plumbing ───────────────────────────────────────────────────────────────


def fail(message: str) -> "NoReturn":  # type: ignore[name-defined]
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)


def request(
    method: str,
    url: str,
    *,
    token: str | None = None,
    body: Any = None,
) -> tuple[int, Any]:
    """Returns (status, parsed body or None). Does not raise on a non-2xx."""
    data = None if body is None else json.dumps(body).encode("utf-8")
    headers = {"accept": "application/json"}
    if data is not None:
        headers["content-type"] = "application/json"
    if token is not None:
        headers["authorization"] = f"Bearer {token}"

    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as response:
            raw = response.read()
            parsed = json.loads(raw) if raw else None
            return response.status, parsed
    except urllib.error.HTTPError as error:
        raw = error.read()
        try:
            parsed = json.loads(raw) if raw else None
        except json.JSONDecodeError:
            parsed = {"title": raw.decode("utf-8", "replace")[:500]}
        return error.code, parsed
    except urllib.error.URLError as error:
        fail(
            f"could not reach {url}: {error.reason}\n"
            "Is the API running? Try: dotnet run --project src/Host/MyGym.Api"
        )


def describe(status: int, body: Any) -> str:
    """Turns a ProblemDetails response into one readable line."""
    if isinstance(body, dict):
        code = body.get("errorCode")
        title = body.get("title") or ""
        detail = f"{code}: {title}" if code else title
        errors = body.get("errors")
        if errors:
            detail += f" {json.dumps(errors)}"
        return f"HTTP {status} — {detail}".strip()
    return f"HTTP {status}"


# ── authentication ─────────────────────────────────────────────────────────


def sign_init_data(bot_token: str, user: dict[str, Any], auth_date: int) -> str:
    """
    Builds a signed initData string the way Telegram does, and the way
    TelegramInitDataValidator.cs verifies it:

        secret = HMAC_SHA256(key="WebAppData", message=bot_token)
        hash   = HMAC_SHA256(key=secret, message=data_check_string)

    where data_check_string is every parameter except `hash`, sorted by key and joined with
    newlines.
    """
    params = {
        "auth_date": str(auth_date),
        "query_id": "AAseed_script",
        "user": json.dumps(user, separators=(",", ":"), ensure_ascii=False),
    }

    data_check_string = "\n".join(f"{k}={params[k]}" for k in sorted(params))

    secret = hmac.new(b"WebAppData", bot_token.encode("utf-8"), hashlib.sha256).digest()
    signature = hmac.new(secret, data_check_string.encode("utf-8"), hashlib.sha256).hexdigest()

    from urllib.parse import urlencode

    return urlencode({**{k: params[k] for k in sorted(params)}, "hash": signature})


def sign_in(api: str, bot_token: str, user_id: int, first_name: str) -> str:
    init_data = sign_init_data(
        bot_token,
        {"id": user_id, "first_name": first_name, "language_code": "uz"},
        int(time.time()),
    )

    status, body = request("POST", f"{api}/api/auth/telegram", body={"initData": init_data})
    if status != 200 or not isinstance(body, dict):
        hint = ""
        if isinstance(body, dict) and body.get("errorCode") == "auth.init_data_signature_invalid":
            hint = (
                "\nThe signature did not verify. BOT_TOKEN here must be the same token the API is "
                "configured with (Telegram:BotToken)."
            )
        fail(f"sign-in failed: {describe(status, body)}{hint}")

    print(f"signed in as Telegram user {user_id}")
    return str(body["accessToken"])


# ── the program ────────────────────────────────────────────────────────────


def load_program(path: Path) -> dict[str, Any]:
    if not path.exists():
        fail(f"program file not found: {path}")
    try:
        program = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        fail(f"{path} is not valid JSON: {error}")

    if not isinstance(program, dict) or "days" not in program or "name" not in program:
        fail(f"{path} must be an object with 'name' and 'days'")
    return program


def resolve_slugs(api: str, token: str) -> dict[str, str]:
    status, body = request("GET", f"{api}/api/exercises", token=token)
    if status != 200 or not isinstance(body, list):
        fail(f"could not load the exercise catalog: {describe(status, body)}")

    by_slug = {item["slug"]: item["id"] for item in body if isinstance(item, dict)}
    print(f"catalog has {len(by_slug)} exercises")
    return by_slug


def build_request(program: dict[str, Any], by_slug: dict[str, str]) -> dict[str, Any]:
    """Turns slugs into ids and drops the commentary, producing a SaveProgramRequest."""
    missing: list[str] = []
    days: list[dict[str, Any]] = []
    working_sets = 0

    for day in program["days"]:
        prescriptions: list[dict[str, Any]] = []

        for item in day["prescriptions"]:
            slug = item["exercise"]
            alternative = item.get("alternative")

            for name in (slug, alternative):
                if name is not None and name not in by_slug:
                    missing.append(name)

            working_sets += int(item["workingSetCount"])

            prescriptions.append(
                {
                    "exerciseId": by_slug.get(slug, ""),
                    "alternativeExerciseId": None if alternative is None else by_slug.get(alternative, ""),
                    "workingSetCount": item["workingSetCount"],
                    "repMin": item["repMin"],
                    "repMax": item["repMax"],
                    "restMinSeconds": item["restMinSeconds"],
                    "restMaxSeconds": item["restMaxSeconds"],
                    "warmup": [
                        {
                            "percentOfWorking": step["percentOfWorking"],
                            "reps": step["reps"],
                            "isOptional": step["isOptional"],
                        }
                        for step in item.get("warmup", [])
                    ],
                    "notes": item.get("notes"),
                }
            )

        days.append(
            {
                "label": day["label"],
                "dayOfWeek": day.get("dayOfWeek"),
                "prescriptions": prescriptions,
            }
        )

    if missing:
        unique = sorted(set(missing))
        fail(
            "these slugs are not in the exercise catalog: "
            + ", ".join(unique)
            + "\nCheck CatalogSeed.cs, or GET /api/exercises for the full list."
        )

    print(f"program '{program['name']}': {len(days)} days, {working_sets} working sets a week")
    return {"name": program["name"], "days": days}


def save(api: str, token: str, payload: dict[str, Any]) -> dict[str, Any]:
    status, existing = request("GET", f"{api}/api/programs", token=token)
    if status != 200 or not isinstance(existing, list):
        fail(f"could not list programs: {describe(status, existing)}")

    same_name = next(
        (p for p in existing if isinstance(p, dict) and p.get("name") == payload["name"]),
        None,
    )

    if same_name is None:
        status, body = request("POST", f"{api}/api/programs", token=token, body=payload)
        if status != 201 or not isinstance(body, dict):
            fail(f"could not create the program: {describe(status, body)}")
        print(f"created program {body['id']}")
        return body

    # Replace rather than duplicate, so re-running after an edit is the edit loop. An archived
    # program cannot be edited, which the API reports as program.not_editable.
    program_id = same_name["id"]
    status, body = request("PUT", f"{api}/api/programs/{program_id}", token=token, body=payload)
    if status != 200 or not isinstance(body, dict):
        hint = ""
        if isinstance(body, dict) and body.get("errorCode") == "program.not_editable":
            hint = (
                f"\nProgram {program_id} is archived. Rename this program in the JSON to create a "
                "fresh one."
            )
        fail(f"could not update the existing program: {describe(status, body)}{hint}")
    print(f"updated program {program_id} to revision {body.get('revisionNumber')}")
    return body


def activate(api: str, token: str, program: dict[str, Any]) -> None:
    status, body = request(
        "POST", f"{api}/api/programs/{program['id']}/activate", token=token
    )

    if status == 204:
        print("activated")
        return
    # Already being the active program is the desired end state, not a failure.
    if status == 409 and isinstance(body, dict) and body.get("errorCode") == "program.already_active":
        print("already active")
        return
    fail(f"could not activate: {describe(status, body)}")


def set_profile(api: str, token: str, program: dict[str, Any]) -> None:
    status, current = request("GET", f"{api}/api/me", token=token)
    if status != 200 or not isinstance(current, dict):
        fail(f"could not read the profile: {describe(status, current)}")

    bodyweight = os.environ.get("BODYWEIGHT")
    # PATCH /api/me is a full replace, so every field is sent -- omitting one clears it.
    payload = {
        "birthYear": current.get("birthYear"),
        "bodyweight": float(bodyweight) if bodyweight else current.get("bodyweight"),
        "preferredUnit": current.get("preferredUnit", "Kilograms"),
        "loadingIncrement": current.get("loadingIncrement", 2.5),
        "experienceLevel": "Intermediate",
        "availableDaysPerWeek": len(program.get("days", [])) or None,
        "sessionMinutes": current.get("sessionMinutes"),
        "timeZoneId": os.environ.get("TIME_ZONE", "Asia/Tashkent"),
    }

    status, body = request("PATCH", f"{api}/api/me", token=token, body=payload)
    if status != 200:
        fail(f"could not update the profile: {describe(status, body)}")
    print("profile updated")


def main() -> None:
    bot_token = os.environ.get("BOT_TOKEN")
    raw_user_id = os.environ.get("TELEGRAM_USER_ID")

    if not bot_token:
        fail("BOT_TOKEN is required — the same token the API is configured with.")
    if not raw_user_id:
        fail("TELEGRAM_USER_ID is required — your numeric Telegram id (ask @userinfobot).")
    try:
        user_id = int(raw_user_id)
    except ValueError:
        fail(f"TELEGRAM_USER_ID must be a number, got {raw_user_id!r}")

    api = os.environ.get("API_BASE_URL", "http://127.0.0.1:5199").rstrip("/")
    program_file = Path(
        os.environ.get("PROGRAM_FILE", REPO_ROOT / "scripts" / "programs" / "akmal-3day.json")
    )

    source = load_program(program_file)
    token = sign_in(api, bot_token, user_id, os.environ.get("FIRST_NAME", "Athlete"))
    payload = build_request(source, resolve_slugs(api, token))
    program = save(api, token, payload)
    activate(api, token, program)

    if os.environ.get("SET_PROFILE") == "1":
        set_profile(api, token, program)

    print()
    print("Done. Open the Mini App and today's day will be there.")
    print(f"  program id  {program['id']}")
    for day in program.get("days", []):
        print(f"  {day.get('dayOfWeek') or '—':<10} {day.get('label')}  ({day['id']})")


if __name__ == "__main__":
    main()
