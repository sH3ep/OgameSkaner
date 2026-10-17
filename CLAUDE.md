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
    - `GalaxyPageParser` is line-based on fixed markup markers (row start, `planet-index`, player-name span, `col-moon` + next-line length), ported from the original app. The player's line also gives the status classes (`isInactive7`, `isVacation`, …), the rank (the tooltip's `/statistics?rel=` link) and, on the line before it, the player id.
    - When content isn't a galaxy or fleet page (e.g. a login redirect), the parsers throw `SessionExpiredException`.
  - **Storage:** everything lives in `%AppData%\OgameSkaner` (`IAppPaths`).
    - `profiles.json`: universes plus the selected one.
    - `sessions.json`: values protected through `ISecretProtector`, which the App implements with DPAPI.
    - `universes/<name>/planets.json`: compact JSON, written atomically by `JsonFile`. Version 2 added each planet's player status, id and rank; version 1 files load without them (rescan to fill them). Version 3 added the scanned systems (`PlanetDatabase.ScannedSystems`, also empty ones); older files only know the systems that have planets, so rescan to find fully empty systems.
    - `universes/<name>/build-settings.json`: the Builder page's strategy and last run options (`IBuildSettingsStore`), including the planets the user unticked (`ExcludedPlanetIds`, so new colonies are included by default).
    - `optimizer-results.json` (shared): the Optimizer page's last setup and the best order per setup.
    - `schemas.json` (shared by all universes): the schema library; `universes/<name>/schema-assignments.json`: which schema each planet follows and whether it runs.
    - `universes/<name>/own-planets.json`: the player's own planets (id, name, coordinates), refreshed by `OwnPlanetService` from the Builder page and at the start of every build run.
    - `universes/<name>/raid-settings.json`, `espionage-reports.json` (newest report per target) and `raids.json` (spies and attacks sent, newest 1000): the Raids page (`IRaidStore`).
  - **Build automation:** `Domain/Buildings` (catalog with server ids, cost factors and energy formulas; strategy), `Services/Building/BuildPlanner` (pure: cheapest affordable next upgrade by weighted value metal 1 / crystal 1.5 / deuterium 3, skipping locked buildings; solar only when energy runs out; now only used for the buildings the simulation doesn't know) and `SimulatedBuildRunner`, the Builder's only mode (see "Builder's Simulated mode" below; the old Single/Auto modes were removed as inferior).
    - A run works on the planets ticked on the Builder page (`BuildRequest.Planets`) and always switches the game back to the planet that was selected before it (`IOgamexClient.SelectPlanetAsync`, also after errors or Stop).
    - Endpoints and server quirks are documented in `docs/ogamex-build-api.md`. State comes from `/building/resource` and `/building/facility` (`BuildingPageParser`), switching planet with `?planet=<guid>` (global server-side state, the browser follows); upgrades are `POST /building/increasebuilding` with PascalCase `{BuildingType, __PlanetId}`.
    - Costs: the next level's cost is read from the page (OgameX mine costs don't follow a formula); the catalog factor only extrapolates further levels.
    - Storages have no rules: `StorageFillPercent` (default 70, 0 = off) is applied inside the simulation (see below). `BuildPlanner`'s own page-based storage rule is off when the runner uses it for the other buildings.
    - Only planets are automated (moons get their own automation later): the parser skips sidebar links marked as moons and the client refuses a page without a metal mine. Free fields (`remainingFieldsQueueIncluded`, resources page only) cap the plan; vacation mode (`window.isVacation`) stops the run for that planet.
    - Energy: the solar plant is driven by energy, not price. The starting point is the game's balance (`resources.energy − resources.energyConsumption`, bonuses from research/officers/class included); only the difference of planned levels comes from base formulas (`EnergyFormulas.Projected`). The per-building energy numbers on the page are base formulas shifted by one level, so don't use them.
    - The server checks each new queue item against the planet's actual resources and takes them only when the item starts. It answers `IsSuccess: true` but **silently ignores** an unaffordable item, so the service verifies every queued item on the next state read. The queue holds 15 (running + 14 waiting).
  - **Build schemas:** `Domain/Schemas` (`BuildSchema` = ordered absolute steps like "Crystal Mine 2" + `SchemaRules`), `Services/Schemas/SchemaPlanner` (pure: next action for a planet – solar plant then solar satellites when energy runs out, storage at fill %, Robotics/Nanite when the saved construction time of the next 15 steps exceeds their build time + cost as production time, then the next step in strict order) and `SchemaRunner` (continuous virtual queue: each cycle pushes what the planner allows, switches back to the player's planet, waits the check interval).
    - The game **drops a queued item whose resources are missing when its turn comes**, so the schema planner only pushes an item when resources cover it plus all waiting queue items, and keeps the game's queue shallow (`MaxServerQueueDepth`, default 3).
    - Shared planning helpers (page-anchored costs/durations, storage rule) live in `Services/Building/PlanningRules.cs`. The App's `AutomationActivity` lets only one of Builder/Schemas run at a time.
  - **Build order optimizer:** `Domain/Optimizer/GameModel` (OgameX formulas measured on genesis: costs, construction times with Robotics `(1+R)·1.05^R` and Nanite `1.8^N`, production, energy, storage capacity, satellites; see the docs' "Formulas for the optimizer" and "Account bonuses"). Its parameters are `AccountBonuses` (research/officers/academy/premium/ranking add up, class bonus multiplies; construction/ship speeds divide time), read from `/resource`, `/officer` and `/academy` by `GetAccountBonusesAsync` before every run, and `PlanetConditions` (slot bonus, temperature). `AccountBonuses.Calibration` holds the bonuses the measurements were taken with, which the model divides out; `Services/Optimizer/BuildSimulator` (event simulation of one order on a fresh planet: one building at a time, satellites in parallel, resources taken at the start, production capped by storage) and `BuildOrderOptimizer` (N independent runs on below-normal-priority threads, 90% of the cores by default: randomized greedy start + simulated annealing). Speed: the simulator state is one value type (`SimState`), saved every 8 genes in an `EvaluationTrace`; a candidate resumes from the last checkpoint before the first gene its mutation changed (moved back further when an earlier satellite gene looked ahead past it), bit-for-bit equal to a full run (`Resuming_from_a_checkpoint_gives_exactly_the_full_result`). Satellites finished while energy already covers the mines are counted in O(1). Core is compiled with `Optimize=true` even in Debug (unoptimized code was ~6x slower). Results: `optimizer-results.json` in the app folder, best order per `OptimizerSetup.Key` (includes `GameModel.Version`; bump it when a formula changes). The Optimizer page can save an order as a schema.
  - **Builder's Simulated mode:** `Services/Building/SimulatedBuildRunner` (continuous, like the schema runner) plans each planet with the optimizer from its real state (`OptimizerSetup.Start` = `PlanetStart`: levels, resources, satellites, queue with the running item's remaining seconds; the simulator finishes the queue first and counts time from now) to the strategy's targets, with `CoverEnergy` (done only when energy covers the mines; satellites for an energy shortage in the real queue are ordered at time 0, the plan's first step), `StorageFillPercent` (the simulator builds a storage level before an item that would leave its resource at ≥ that share of capacity, so the storage is in the plan and the game queue at the right point; not applied to the real queue it finishes first) and per-planet corrections (extra energy, production ratios vs. the page). `SimulatedPlanner` (pure) builds the setup and picks what to push (resources cover it plus all waiting items). It re-plans whenever the planet changed, seeds the search with the previous plan, and sleeps until the next item is due (10 s–5 min). Temperature comes from `/home` (`OverviewPageParser`).
  - **Raids (inactive farmer):** endpoints in `docs/ogamex-raid-api.md`.
    - `Services/Raiding/RaidPlanner` (pure): candidates are raidable inactives from the galaxy database (`PlayerStatus.IsRaidable`), best rank first; each gets a `RaidTargetState` from its newest report and the fleets sent (spy when there's no report, it's older than `ReportMaxAge` or from before our last attack; ready when the report's defense and fleet points are within the limits and the loot ≥ `MinLoot`).
    - Reports: `EspionageReportParser` reads the list (`/messages/messagedata?MessageCategoryType=FLEET_ESPIONAGE`) and the detail page (`/messages/spyreportdetail`). A section's header shows **points** (`Defenses (2.000)` = 1,000 missile launchers), which is what the limits use; a section the probes didn't see has no number (`Unseen`). Labels follow the language cookie, so parsing goes by structure.
    - `RaidRunner`: each cycle reads slots (`Fleets: x/y`) and new reports, attacks ready targets (most loot first) and spies on the next ones, both within `FleetSlotUsePercent` of the slots, then sleeps until a report is due or a fleet returns. Ships = loot × (1 + margin) / cargo capacity, where the capacity comes from the fleet page's tooltip (technology included). Every send is checked first with the read-only briefing `/fleet/fleetpage3data` (flight time, possible missions, the player's status).
    - `/fleet/submitfleet` has **no origin**: fleets leave from the server-side current planet. `OgamexClient.SendFleetAsync(mission, origin)` therefore switches with `fleet?planet=` and submits under `_planetSwitch`, a lock every planet-switching request takes, so the Builder can run at the same time. Mission ids: 5 = Deploy (the fleet escape), 6 = Spy, 8 = Attack.
  - **`PlanetDatabase`:** a thread-safe in-memory store keyed by solar system. `JsonPlanetRepository.GetAsync` returns the **same instance** per universe, so the scanner and the players page share data.
  - **Colonization:** `Services/ColonizationSearch` (pure) lists free slots (positions 1–15) in a galaxy/system/slot range. A slot counts as free when its system is in `ScannedSystems` and no planet was seen there. Free slots aren't stored. `ColonizationService` sends one Colony Ship (mission 2) from the nearest own planet that has one (fleet pages are read nearest first). It checks the briefing for `Colonize` first and switches back to the planet selected before.
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
