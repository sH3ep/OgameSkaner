# Licensing and copy protection (plan, not implemented)

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
