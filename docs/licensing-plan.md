# Licensing and copy protection

Layers 1 and 2, the App side and the license server (`server/license-worker`, a Cloudflare Worker with Paddle webhooks) are
implemented (see "Implemented" at the end); layers 3 and 4 are not, and the server isn't deployed yet.

Goal: sell the app as a monthly subscription and make it hard (not impossible) to use without paying.

## Decisions so far

- **The app ships as it is**: all game traffic and all planning stay on the customer's PC. Game requests must
  come from the customer's computer (their IP, their cookies), so no server ever talks to OgameX.
  Moving the planners/optimizer to a server was considered (the only real protection) and rejected for now.
- Protection is **layered deterrence**: a .NET app can always be decompiled (dnSpy/ILSpy) and patched in the end;
  the aim is to make cracking slow and not worth it, and pirated copies short-lived.
- Users use the app on their own responsibility (stated in the app's terms, accepted with a checkbox on first run).
  Open point: the OgameX rules on bots; the server owner isn't bound by our terms and can ban accounts or break the app.
  Consider asking the owner. Not legal advice; get the terms checked before selling.
- Payments: a merchant of record (**Paddle** or **Lemon Squeezy**) handles EU VAT, invoices and renewals; its
  webhooks drive the license server.

## Layers (in order of value)

### 1. Signed license token bound to the OgameX account

- A license server (**Keygen.sh**, or our own ASP.NET minimal API fed by the payment webhooks) issues a token
  signed with **ECDSA P-256** (`System.Security.Cryptography.ECDsa`, built into .NET 8, no package needed).
- Payload: license id, allowed OgameX player id(s) and universe(s), machine hash, issued/expiry time
  (~72 h, so short outages don't lock anyone out), minimum app version.
- The app embeds only the **public key** (never a secret in the client; compare `LegacyTokenDecryptor`'s
  hard-coded AES key, found in a minute).
- Renewed on start and every few hours; stored with DPAPI like `sessions.json`.
- **Account binding:** the client already reads the player id from game pages. When it doesn't match the token,
  automation stops, so a shared key is useless for another account. Tiers can allow N game accounts.
- **Clock rollback:** remember the last server time seen; refuse a token when the PC clock goes back past it.

### 2. Many independent checks instead of one gate

A single `if (licensed)` is patched in a minute. Each check verifies the signature itself, through slightly
different code:

- `App.OnStartup`
- `OgamexClientFactory` (no client without a valid token)
- `ActionQueue.RunAsync` (groups refused without a token)
- a few runners/planners: `SimulatedPlanner`, `RaidPlanner`, `BuildOrderOptimizer`

Failures stay visible and honest for paying users ("license expired"), but the checks don't share one path.

### 3. Commercial obfuscator

**.NET Reactor** or **Eazfuscator.NET** (ConfuserEx is abandoned and undone by de4dot; Obfuscar is weak).
Enable: symbol renaming, string encryption (endpoints, markup markers, the public key), control flow
obfuscation, method body encryption (Reactor's NecroBit), anti-tamper, anti-debug.

Required exclusions (otherwise bindings and serialization break silently):

- view models and their bound properties, types named in XAML
- JSON DTOs (`Ogamex/Dto.cs`) and every settings/store class serialized to JSON
- `CommunityToolkit.Mvvm` generated members
- anything resolved by reflection or DI by name

Use `[Obfuscation(Exclude = true)]` or the tool's rules file, then test every page.
Native AOT isn't an option (WPF doesn't support it); ReadyToRun still ships the IL.

### 4. Packaging

- `dotnet publish` self-contained, single-file, compressed: one exe.
- **Authenticode code-signing certificate**: no SmartScreen warnings, and anti-tamper can check the signature.

### 5. Server-side controls

- **Minimum version:** the server refuses tokens for old builds; after a crack appears, ship an update.
- **Revocation:** a leaked key is disabled and stops working within one token lifetime.
- Optional: serve the parsers' markup markers/endpoints as signed config, so cracked builds break whenever
  OgameX changes its HTML.

## Where it goes in the code

- **Core** (plain .NET, stays free of WPF/Windows APIs): `Licensing/` with `LicenseToken`, the verifier
  (`ECDsa`), `ILicenseService`, the checks in the factory, the queue and the planners. Tests with fake tokens
  signed by a test key.
- **App:** machine fingerprint, DPAPI token storage, activation dialog (through `IDialogService`),
  license status in the sidebar, terms checkbox on first run.
- **Separate project:** the license server (or the Keygen.sh setup) plus the payment webhooks.

## Suggested first step

Layers 1 and 2 in Core: token format, verifier, `ILicenseService`, the checks wired into
`OgamexClientFactory` and `ActionQueue`, tests. It works against a stub server until Keygen.sh or our own
server is chosen; the other layers build on it.

## Implemented

- **Core `Licensing/`:** `LicensePayload` (license id, machine, bound `Accounts`, `MaxAccounts` (0 = any account),
  issued/expiry, `MinAppVersion`, customer, plan), `LicenseToken` (`OSL1.<payload>.<signature>`, base64url; camelCase
  JSON; ECDSA P-256/SHA-256, IEEE P1363), `LicenseVerifier` (states `Missing`, `Valid`, `Invalid`, `WrongMachine`,
  `ClockTurnedBack` (PC clock more than 10 min before the issue time), `Expired`, `Outdated`), `LicenseService`
  (`ILicenseService`: status, activation, import, renewal, terms, account binding), `JsonLicenseStore` (`license.json`,
  key and token through `ISecretProtector`), `HttpLicenseServer`, `LicenseTerms` (draft text, `Version`).
- **Public key:** `LicenseKeys.Production`. The private key was made with `ogs-license keygen` and is kept outside the
  repo (`%USERPROFILE%\.ogameskaner\license-signing-key.pem`; back it up). A new key pair invalidates every token.
- **Checks** (`LicenseChecks.DemandA/B/C`, three different implementations, each verifying the signature itself):
  `OgamexClientFactory.Create` (A), `ActionQueue.RunAsync`/`RunUrgentAsync` (B), `BuildOrderOptimizer.RunAsync` (A),
  `RaidRunner.RunAsync` (C), `LicenseService.ConfirmAccountAsync` (C), plus `App.OnStartup` (`LicenseVerifier`).
  A refused check throws `LicenseException` (not an `OgamexException`, so the runners stop with its message).
- **Account binding:** `OgamexClient` reads `window.currentPlayerId` from every full game page (`PageIdentityParser`)
  and confirms a new player with `ConfirmAccountAsync`: covered → on; not covered while the license has a free
  account → the server binds it (new token); else `LicenseException`.
- **Server contract** (`HttpLicenseServer`, `License:ServerUrl`): `POST {ServerUrl}/v1/token` with
  `{ licenseKey, machine, appVersion, account?: { universe, playerId } }` → `{ token }`, or an error status with
  `{ error }` shown to the player. Activation, renewal (`License:RenewEvery`, 4 h; `RetryAfter` 15 min) and binding an
  account are the same call. The server should issue tokens valid ~72 h and refuse cancelled keys, too many machines
  and builds below the minimum version.
- **App:** `WindowsMachineFingerprint` (SHA-256 of the Windows MachineGuid, `XXXX-XXXX-XXXX-XXXX-XXXX`), the license
  window (`LicenseWindow`/`LicenseViewModel`: terms checkbox, key activation, machine id, token import), shown at start
  while the terms aren't accepted or the license isn't valid (closing it without one ends the app), and from the
  sidebar's "License…" button; the sidebar shows the status.
- **`tools/OgameSkaner.LicenseTool`** (`ogs-license`, never shipped): `keygen`, `pubkey`, `issue` (`--machine` from the
  license window, `--account <universe>:<playerId>` and/or `--max-accounts`, `--days`, `--min-version`), `inspect`, and
  `admin …` for the server's admin API (`OGS_LICENSE_SERVER`, `OGS_LICENSE_ADMIN_TOKEN`). Without a server, tokens are
  issued with `issue` and pasted into the license window.
- **License server** (`server/license-worker`, TypeScript, Cloudflare Workers + D1; setup, deployment and operation in
  its `README.md`):
  - Chosen over an ASP.NET server: nothing to run or patch, free tier enough, WebCrypto's ECDSA P-256 signature is
    already the P1363 format the app verifies. The cost is the token format existing twice (`src/token.ts` and
    `LicenseToken`/`LicensePayload`); change them together. Checked end to end: tokens from the worker, signed with the
    production key, verify in .NET, and `HttpLicenseServer` activates, renews and binds accounts against it.
  - Licenses (key stored as SHA-256 only), machines (`max_machines`; a PC unused for `MACHINE_RELEASE_DAYS` frees its
    place), game accounts (`max_accounts`), an event log, Paddle event ids. Plans and their Paddle prices are in
    `wrangler.jsonc` (`PLANS`).
  - `POST /v1/token` refuses unknown, revoked, cancelled and expired licenses, old builds (`MIN_APP_VERSION`, HTTP 426),
    a PC over the limit and an account over the limit, with a message the app shows. Tokens last `TOKEN_HOURS` (72),
    at most to `valid_until`. Rate-limited per machine.
  - `POST /webhooks/paddle` (Paddle Billing, signature checked): `subscription.*` events create the license (key mailed
    through Resend when configured) and keep status and `valid_until` (period end + `GRACE_DAYS`; past due at most
    the grace days) in step. Duplicates and older events are skipped; a license revoked by hand stays revoked.
  - `/admin/…` (bearer `ADMIN_TOKEN`): public key, list, create, change, new key, free a PC, unbind an account.
  - Tests: `npm test` (Vitest in the Workers runtime with local D1).
