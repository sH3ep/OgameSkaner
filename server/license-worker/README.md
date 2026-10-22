# OgameSkaner license server

A Cloudflare Worker with a D1 (SQLite) database. It signs the license tokens the app checks
(`OgameSkaner.Core.Licensing`), keeps licenses in step with Paddle subscriptions and lets the owner manage them.
Background: `docs/licensing-plan.md`.

| Endpoint | Who | What |
|---|---|---|
| `POST /v1/token` | the app (`HttpLicenseServer`) | activation, renewal (every 4 h) and binding a game account: `{ licenseKey, machine, appVersion, account? }` → `{ token }` or `{ error }` |
| `POST /webhooks/paddle` | Paddle | `subscription.*` events create and update licenses |
| `/admin/…` | you (`ogs-license admin …`) | list, create, change, revoke, new key, free a PC, unbind an account (see `src/admin.ts`) |
| `GET /health` | monitoring | `{ ok: true }` |

## How a token request is decided

1. The key must exist (only its SHA-256 is stored) and the license must be `active` or `past_due`. It must also be within
   `valid_until`, which is the paid period plus `GRACE_DAYS`, or no end for licenses made by hand.
2. `appVersion` must be at least `MIN_APP_VERSION`, and the token carries that minimum.
3. **PCs:** a new machine gets a place while the license has fewer than `max_machines`. Otherwise the machine unused
   longest gives up its place once it hasn't been seen for `MACHINE_RELEASE_DAYS`. Without such a machine the request
   is refused.
4. **Game accounts:** a requested account is bound while fewer than `max_accounts` are bound. 0 means any account, with
   nothing bound.
5. The token is valid for `TOKEN_HOURS` (72), or until `valid_until` if that comes sooner. It is signed with ECDSA
   P-256. WebCrypto's signature format (r‖s) is the IEEE P1363 format the app verifies.

New PCs, bound accounts, refusals and Paddle and admin changes go to `events`. Renewals only update the machine's
`last_seen`, `requests`, IP and country.

## Deploying

Run everything from this folder, in PowerShell.

1. `npm install`, then `npx wrangler login`.
2. `npx wrangler d1 create ogameskaner-licenses` and put the printed `database_id` into `wrangler.jsonc`.
3. `npm run db:migrate:remote` creates the tables. A schema change is a new file in `migrations/`; never edit an
   applied one.
4. Set the secrets. They are write-only in Cloudflare and never in the repo.
   ```powershell
   Get-Content -Raw $env:USERPROFILE\.ogameskaner\license-signing-key.pem | npx wrangler secret put SIGNING_KEY
   py -c "import secrets; print(secrets.token_urlsafe(32))"   # remember it, then:
   npx wrangler secret put ADMIN_TOKEN
   ```
5. `npm run deploy` prints the URL, e.g. `https://ogameskaner-license.<account>.workers.dev`. A custom domain such as
   `license.example.com` can be added under the Worker's *Domains & Routes*.
6. Check that the deployed key matches the app's key:
   ```powershell
   $env:OGS_LICENSE_SERVER = "https://ogameskaner-license.<account>.workers.dev"
   $env:OGS_LICENSE_ADMIN_TOKEN = "<the admin token>"
   dotnet run --project ..\..\tools\OgameSkaner.LicenseTool -- admin public-key   # = LicenseKeys.Production
   ```
7. In the app's `appsettings.json`, set `"License": { "ServerUrl": "<that URL>" }` and ship the build. From then on the
   license window activates with a key and the app renews by itself.

Optional hardening: put `/admin/*` behind Cloudflare Access (Zero Trust, free for a few users) on top of the token.

## Paddle

1. Create the product and its monthly prices in Paddle, sandbox first. Put each price id into its plan's
   `paddlePriceIds` in `wrangler.jsonc` (`PLANS`). A price no plan lists gets `DEFAULT_PLAN` and an `unknown_price`
   event.
2. *Developer tools → Notifications → New destination*:
   - URL: `<server>/webhooks/paddle`
   - Events: `subscription.created`, `.activated`, `.updated`, `.trialing`, `.past_due`, `.paused`, `.resumed` and
     `.canceled`
   - Put its secret key into `npx wrangler secret put PADDLE_WEBHOOK_SECRET`.
3. An API key with customer read access goes into `PADDLE_API_KEY`. The webhook doesn't carry the buyer's e-mail address,
   so the server looks it up with this key.
4. Live: set `PADDLE_ENVIRONMENT` to `"production"` and use the live secret and key.

What a subscription does to its license:

| Paddle subscription | License |
|---|---|
| `active` or `trialing` | `active` until the period ends, plus the grace days |
| `past_due` (Paddle retries the payment) | still works for at most the grace days |
| `canceled` or `paused` | `cancelled`, and tokens are refused. A token already issued runs out within `TOKEN_HOURS` |

A license you revoke by hand stays revoked whatever Paddle sends. Duplicate deliveries are applied once, and events
older than the last one applied are skipped.

## The buyer's key

On a new subscription the server makes a key and e-mails it through [Resend](https://resend.com). For that, verify
your sending domain there, then set the `RESEND_API_KEY` secret and the `MAIL_FROM` var (e.g.
`"OgameSkaner <license@example.com>"`).

Without mail, or when sending fails (the `key_not_mailed` or `key_mail_failed` event), the key is lost, because only
its hash is stored. Make a new one with `ogs-license admin rotate-key <id>` and send it yourself; `--mail true` mails it
when mail is set up.

## Everyday admin

```powershell
ogs-license admin list --status active
ogs-license admin show L-XXXXXXXXXX                            # PCs, accounts, events
ogs-license admin create --email a@b.c --plan pro --days 30    # sold by hand, a tester, yourself (no --days = no end)
ogs-license admin set L-XXXXXXXXXX --status revoked            # leaked key, chargeback
ogs-license admin release L-XXXXXXXXXX ABCD-EF01-2345-6789-ABCD  # customer changed PC
ogs-license admin unbind L-XXXXXXXXXX genesis <player id>      # customer moved game account
ogs-license admin rotate-key L-XXXXXXXXXX --mail true
```

Signs of a shared key in `show`: many `machine_added` or `refused` events from different IPs and countries.

## Developing

- `npm test` runs Vitest inside the Workers runtime with a real local D1 and a key made for each run.
- `npm run typecheck` checks the types.
- `npm run db:migrate:local` once, then `npm run dev` serves on `http://localhost:8787`. Put the secrets in `.dev.vars`
  (gitignored) or the environment, e.g. `SIGNING_KEY` from the PEM and any `ADMIN_TOKEN`. Point a debug build there
  with `License:ServerUrl` in `%AppData%\OgameSkaner\appsettings.json`.
- The token format exists twice: `src/token.ts` here and `LicenseToken`/`LicensePayload` in Core. Change them together,
  and check a token from `npm run dev` with `ogs-license inspect <token> --key <pem>`.

## Backups and limits

- D1 keeps a point-in-time history (Time Travel). `npx wrangler d1 export ogameskaner-licenses --remote --output
  licenses.sql` makes a full dump.
- At 6 renewals a day per PC, the free Workers and D1 tiers cover thousands of customers. Check Cloudflare's current
  limits before relying on that.
