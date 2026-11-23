# scripts

Three scripts. None of them needs a database; `set-webhook.sh` needs the internet, the other two
do not.

## `dump-openapi.sh`

Writes the API's OpenAPI document to a file.

```bash
bash scripts/dump-openapi.sh clients/mini-app/openapi.json
```

Starts the host in Development with migrations disabled and placeholder secrets, fetches
`/openapi/v1.json`, and exits. Needs the .NET SDK but **no PostgreSQL** — the document is derived
from endpoint metadata, not from data.

The Mini App's types are generated from the output; see `clients/mini-app/README.md`.

## `seed-program.sh`

Creates (or updates) a training program and activates it. This is how a program gets into the
database, because Mini App v1 deliberately has no authoring UI.

```bash
BOT_TOKEN=123456:ABC-your-bot-token \
TELEGRAM_USER_ID=123456789 \
  bash scripts/seed-program.sh
```

| Variable | | |
|---|---|---|
| `BOT_TOKEN` | required | Must be **the same token the API is configured with** (`Telegram:BotToken`), or the signature will not verify |
| `TELEGRAM_USER_ID` | required | Your numeric Telegram id — ask [@userinfobot](https://t.me/userinfobot) |
| `API_BASE_URL` | optional | Default `http://127.0.0.1:5199` |
| `PROGRAM_FILE` | optional | Default `scripts/programs/akmal-3day.json` |
| `FIRST_NAME` | optional | Default `Athlete`; only used on first sign-in |
| `SET_PROFILE` | optional | `1` to also update the profile (experience, time zone, training days) |
| `BODYWEIGHT` | optional | Only with `SET_PROFILE=1` |
| `TIME_ZONE` | optional | Default `Asia/Tashkent`; only with `SET_PROFILE=1` |

### How it works, and why

It goes through the **public API**, not the database. The same domain validation an athlete would
hit applies here, so a program this script accepts is a program the app can actually run.

Authentication uses the real signature-checked `/api/auth/telegram` endpoint: the script builds an
`initData` string and signs it with the bot token using Telegram's own derivation. **There is no
seeding back door.** The program ends up owned by the Telegram user whose id you pass, which is why
it has to be your real one — otherwise the Mini App signs in as you and finds nothing.

Exercises are referenced by catalog **slug**, resolved against `GET /api/exercises` at run time, so
the program file stays readable and survives a reseeded database.

**Re-running is safe.** A program whose name already exists is replaced with `PUT` rather than
duplicated, so editing the JSON and running again is the edit loop.

### The program file

`scripts/programs/akmal-3day.json` holds the author's own program: 21 + 20 + 20 = 61 working sets a
week. Copy it to describe a different one and point `PROGRAM_FILE` at the copy.

Two things to know before editing it:

- **`percentOfWorking` is a fraction, not a percentage.** The domain requires `0 < p < 1`, so 53% is
  `0.53`. Percentages must also increase strictly from one warm-up step to the next.
- **Warm-up steps cannot express an absolute weight.** The original program's squat ramp starts with
  an empty bar — about 20 kg — which is not a fraction of anything. `0.25` stands in for it. At a
  100 kg squat that resolves to 25 kg rather than 20.

The file is validated against the real domain rules with no server running, by
`tests/MyGym.Modules.Training.Domain.Tests/SeedProgramFileTests.cs`. A mistyped percentage fails
there as a unit test rather than arriving as an HTTP 422 from a live API — which matters, since the
API cannot currently be started on this machine at all.

## `set-webhook.sh`

Registers the bot's webhook with Telegram and sets the chat menu button to open the Mini App.

**Nothing in the application does this.** The webhook endpoint exists and is secret-guarded, but
Telegram will not send a single update until the URL is registered through the Bot API — a one-time
call per deployment that is easy to forget and produces a bot that simply never answers.

```bash
BOT_TOKEN=123456:ABC \
PUBLIC_URL=https://gym.example.com \
WEBHOOK_SECRET=<same value as the API's Telegram:WebhookSecretToken> \
  bash scripts/set-webhook.sh
```

| Variable | | |
|---|---|---|
| `BOT_TOKEN` | required | From [@BotFather](https://t.me/BotFather) |
| `PUBLIC_URL` | required | The HTTPS origin serving this stack. Must be https — Telegram refuses plain http |
| `WEBHOOK_SECRET` | optional | Must match the API's `Telegram:WebhookSecretToken`. If omitted, one is generated and printed for you to configure |
| `MINIAPP_URL` | optional | Default `PUBLIC_URL` |
| `MENU_BUTTON_TEXT` | optional | Default `MyGym` |
| `SKIP_MENU_BUTTON` | optional | `1` to leave the menu button alone |

Other modes:

```bash
BOT_TOKEN=... bash scripts/set-webhook.sh --show     # read the current registration
BOT_TOKEN=... bash scripts/set-webhook.sh --delete   # stop Telegram sending updates
```

It always reads the registration back with `getWebhookInfo` and prints it, because **`setWebhook`
can answer 200 while delivery still fails** — an unreachable host, a certificate Telegram will not
trust, the API not running. That shows up as `last_error_message`, and nowhere else.

`allowed_updates` is narrowed to `message`, which is all the handler acts on. That is not cosmetic:
the webhook endpoint caps request bodies, and the update types it would otherwise receive are far
larger than a message.

### The order these go in

1. Deploy the stack so `PUBLIC_URL` serves over HTTPS (see **Deployment** in the root `README.md`).
2. `set-webhook.sh` — if it generates a secret, put it in `.env`, restart the API, and run again
   with `WEBHOOK_SECRET` set to the same value.
3. `seed-program.sh` — your program.
4. Send `/start` to the bot.
