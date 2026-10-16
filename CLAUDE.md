# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

WPF desktop helper for the OgameX private OGame server (galaxy scanner, player search with spy actions, fleet escape). Only OgameX is supported; support for other servers was removed deliberately.

## Commands

```powershell
dotnet build                                   # whole solution (OgameSkaner.sln)
dotnet test                                    # xUnit, tests/OgameSkaner.Core.Tests
dotnet test --filter "FullyQualifiedName~GalaxyPageParserTests"   # one class
dotnet test --filter "DisplayName~Parse_ignores_expedition"       # one test
dotnet run --project src/OgameSkaner.App
```

- `OgameSkaner.Core` has `TreatWarningsAsErrors`, so any new warning there breaks the build.
- The app targets `net8.0-windows` with `RollForward=Major`, because this machine has only the 9.x/10.x WindowsDesktop runtimes.
- There is no linter configured.

## Architecture

Two projects plus tests. `Directory.Build.props` enables nullable and implicit usings everywhere.

- **`src/OgameSkaner.Core`** (`net8.0`, must stay free of WPF/Windows APIs):
  - **OgameX client:** `IOgamexClient`, one instance per universe, created by `OgamexClientFactory`.
    - Each client gets its own `CookieContainer` and a handler chain: `LoggingHandler → ThrottlingHandler → BrowserHeadersHandler → SocketsHttpHandler`.
    - `ThrottlingHandler` serializes **all** requests with a jittered minimum gap. Don't add delays elsewhere.
  - **Auth:** browser cookies (`gameAuthToken`, `SessionId`) are read from `ISessionStore` before every request. There is no login endpoint.
  - **JSON bodies:** camelCase (`JsonSerializerDefaults.Web`). This matches what the old RestSharp code sent.
  - **Parsers:** `Ogamex/Parsing/*` are pure static functions.
    - `GalaxyPageParser` is line-based on fixed markup markers (row start, `planet-index`, player-name span, `col-moon` + next-line length), ported from the original app.
    - When content isn't a galaxy or fleet page (e.g. a login redirect), the parsers throw `SessionExpiredException`.
  - **Storage:** everything lives in `%AppData%\OgameSkaner` (`IAppPaths`).
    - `profiles.json`: universes plus the selected one.
    - `sessions.json`: values protected through `ISecretProtector`, which the App implements with DPAPI.
    - `universes/<name>/planets.json`: compact JSON, written atomically by `JsonFile`.
  - **`PlanetDatabase`:** a thread-safe in-memory store keyed by solar system. `JsonPlanetRepository.GetAsync` returns the **same instance** per universe, so the scanner and the players page share data.
  - **`Storage/Legacy/LegacyImporter`:** migrates the old .NET Framework app's `GamesConfiguration.xml` and `DatabaseOgameX{uni}.xml`. Its tokens use the old hard-coded AES key in `LegacyTokenDecryptor`, which is only for migration.
  - **DI:** services are registered in `ServiceCollectionExtensions.AddOgameSkanerCore`.
- **`src/OgameSkaner.App`** (WPF, `Microsoft.Extensions.Hosting`, CommunityToolkit.Mvvm, Serilog):
  - `App.xaml.cs` builds the host and is the composition root. There is no `StartupUri`.
  - Config comes from `appsettings.json`, with an optional per-user override at `%AppData%\OgameSkaner\appsettings.json`. Set `Ogamex:DumpResponsesDirectory` there to capture raw responses as test fixtures.
  - **Pages are view-model-first:**
    - `services.AddPage<TViewModel, TView>("Title")` registers a singleton `PageViewModel` and a `PageDescriptor`.
    - `PageRegistration.RegisterTemplates` generates the implicit DataTemplates.
    - `MainViewModel.CurrentPage` drives a `ContentControl`.
    - Adding a page means a view model, a `UserControl` and one `AddPage` line.
  - **`IUniverseContext`** holds the selected `UniverseProfile` and its `IOgamexClient`. Pages subscribe to `CurrentChanged`; the scanner cancels a running scan there.
  - **View models never show windows or message boxes directly.** They use `IDialogService`.
  - **Cross-page notifications** go through `IMessenger`, for example `SessionSavedMessage` triggers a session re-check.

## Conventions and gotchas

- **New endpoints:**
  - Add a method to `IOgamexClient`/`OgamexClient` and DTOs in `Ogamex/Dto.cs`.
  - Put HTML parsing in `Ogamex/Parsing` with a fixture under `tests/.../Fixtures`, which is copied to the output directory.
  - Test services against `FakeOgamexClient` and `FakePlanetRepository` in `tests/.../Fakes`.
- **Universe names** become a sub-domain (`https://{name}.ogamex.net`). Always validate them with `UniverseProfile.IsValidName`.
- **Implicit usings:** WPF projects don't include `System.IO`, so add `using System.IO;` in App code when needed.
- **Legacy data:** the untracked `OgameSkaner/bin/Debug` folder (leftover of the old project) holds the user's real legacy data and session tokens. Don't delete it. Treat it as read-only and sensitive.
- **Never commit:** local data files (`GamesConfiguration.xml`, `Database*.xml`, `profiles.json`, `sessions.json`) are gitignored.
