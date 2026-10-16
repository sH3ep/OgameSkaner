# OgameSkaner

Desktop helper for the [OgameX](https://ogamex.net) private OGame server:

- **Scanner** – scans a range of the galaxy view and stores who owns which planet (and which have a moon).
- **Players** – searches the scanned planets by player name, sends spy probes to a planet or its moon and exports results to CSV.
- **Utilities** – escape fleet: sends every ship of the current planet to its moon.

## Build & run

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer (the app also runs on a newer desktop runtime).

```powershell
dotnet build
dotnet test
dotnet run --project src/OgameSkaner.App
```

## First steps

1. **Manage universes…** → add a universe by its sub-domain, e.g. `tron` for `https://tron.ogamex.net`.
   Coming from the old version? Use **Import from old version…** and pick the folder that contains
   `GamesConfiguration.xml` (the old `bin\Debug`). Universes, saved sessions and scan results are imported.
2. Log in to the game in your browser, open the developer tools (F12) → *Application* → *Cookies*,
   and paste the `gameAuthToken` and `SessionId` values on the **Scanner** page. The session indicator
   in the sidebar turns green when they are accepted. You can keep playing in the browser.

## Where data lives

Everything is stored per Windows user in `%AppData%\OgameSkaner`:

| File | Content |
|---|---|
| `profiles.json` | configured universes and the selected one |
| `sessions.json` | session cookies, encrypted with Windows DPAPI (readable only by your Windows account) |
| `universes\<name>\planets.json` | scan results |
| `logs\` | daily log files |
| `appsettings.json` | optional overrides of `src/OgameSkaner.App/appsettings.json` |

To capture raw server responses (e.g. for new parser test fixtures), set `"Ogamex": { "DumpResponsesDirectory": "C:\\some\\folder" }` in the override file.

## Architecture

```
src/OgameSkaner.Core        net8.0 – no UI dependencies, fully unit-testable
  Domain/                   records: Coordinates, PlayerPlanet, UniverseProfile, ScanRange, Fleet…
  Ogamex/                   IOgamexClient + OgamexClient (HttpClient), DTOs, exceptions
    Http/                   handler pipeline: logging → throttling → browser headers
    Parsing/                GalaxyPageParser, FleetPageParser (pure functions)
  Services/                 GalaxyScanService, FleetEscapeService, PlanetSearch, CsvExportService
  Storage/                  JSON stores for profiles, sessions and planets; Legacy/ importer
src/OgameSkaner.App         net8.0-windows WPF, MVVM with CommunityToolkit.Mvvm + Microsoft.Extensions.Hosting
  Infrastructure/           DI page registration, IUniverseContext, IDialogService, DPAPI protector
  ViewModels/  Views/
tests/OgameSkaner.Core.Tests  xUnit tests for parsers, storage, importer and services
```

- **One client per universe.** `IUniverseContext` holds the selected universe, its `IOgamexClient` and its
  `PlanetDatabase`; pages react to `CurrentChanged`.
- **All requests go through `ThrottlingHandler`**, which serializes them and keeps a randomized minimum gap
  (`MinRequestInterval` + `RequestJitter`) between calls.
- **View models never touch WPF** windows or message boxes directly — they use `IDialogService`.

### Adding a new feature

1. **New endpoint**: add a method to `IOgamexClient`/`OgamexClient` (request + DTO in `Dto.cs`); put any HTML parsing in
   `Ogamex/Parsing` as a pure static parser and cover it with a fixture-based test.
2. **Logic**: add a service in `Core/Services`, register it in `ServiceCollectionExtensions.AddOgameSkanerCore`, and test it
   against `FakeOgamexClient`.
3. **UI page**: create `XxxViewModel : PageViewModel` and an `XxxView` user control, then register both in `App.xaml.cs`:

   ```csharp
   builder.Services.AddPage<XxxViewModel, XxxView>("Xxx");
   ```

   The page appears in the sidebar, and its view is resolved through an automatically registered DataTemplate.
